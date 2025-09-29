using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using SP.Common.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.ProtocolHandler;

namespace SP.Engine.Server
{
    public enum PeerKind : byte
    {
        None = 0,
        User,
        Server,
    }
    
    public enum PeerState
    {
        NotAuthorized = PeerStateConst.NotAuthorized,
        Authorized = PeerStateConst.Authorized,
        Online = PeerStateConst.Online,
        Offline = PeerStateConst.Offline,        
        Closed = PeerStateConst.Closed,
    }

    internal static class PeerStateConst
    {
        public const int NotAuthorized = 0;
        public const int Authorized = 1;
        public const int Online = 2;
        public const int Offline = 3;
        public const int Closed = 4;        
    }
    
    public interface IPeer
    {
        PeerId Id { get; }
        PeerKind Kind { get; }
        PeerState State { get; }
        IClientSession Session { get; }
        bool Send<TProtocol>(TProtocol data) where TProtocol : IProtocol;
        void Close(CloseReason reason);
    }
    
    public abstract class BasePeer : ReliableMessageProcessor, IPeer, IHandleContext, IDisposable
    {
        private static class PeerIdGenerator
        {
            private static readonly ConcurrentQueue<PeerId> FreePeerIdPool = new();
            private static int _latestPeerId;

            public static PeerId Generate()
            {
                if (_latestPeerId >= int.MaxValue)
                    throw new InvalidOperationException("ID overflow detected");

                if (FreePeerIdPool.TryDequeue(out var peerId))
                    return peerId;
                return (PeerId)Interlocked.Increment(ref _latestPeerId);
            }

            public static void Free(PeerId peerId)
            {
                if (Runtime.PeerId.None != peerId)
                    FreePeerIdPool.Enqueue(peerId);
            }
        }

        private int _stateCode = PeerStateConst.NotAuthorized;
        private bool _disposed;
        private DiffieHellman _diffieHellman;
        private Encryptor _encryptor;
        private IPolicyView _policyView = new SnapshotPolicyView(in PolicyDefaults.Globals);

        public PeerState State => (PeerState)_stateCode;
        public IClientSession Session { get; private set; }
        public EngineConfig Config { get; }
        public PeerId Id { get; private set; }
        public PeerKind Kind { get; } 
        public ILogger Logger { get; }
        public double LatencyAvgMs { get; private set; }
        public double LatencyJitterMs { get; private set; }
        public float PacketLossRate { get; private set; }
        public IEncryptor Encryptor => _encryptor;

        public IPEndPoint LocalEndPoint => Session.LocalEndPoint;
        public IPEndPoint RemoteEndPoint => Session.RemoteEndPoint;
        public bool IsConnected => _stateCode == PeerStateConst.Authorized || _stateCode == PeerStateConst.Online;
        public byte[] DhPublicKey => _diffieHellman.PublicKey;
        
        protected BasePeer(PeerKind peerType, IClientSession session)
        {
            Id = PeerIdGenerator.Generate();
            Kind = peerType;
            Logger = session.Logger;
            Config = session.Config;
            SetSendTimeoutMs(session.Config.Network.SendTimeoutMs);
            SetMaxResendCnt(session.Config.Session.MaxResendCount);
            Session = session;
        }

        protected BasePeer(BasePeer other)
        {
            Id = other.Id;
            Kind = other.Kind;
            Logger = other.Logger;
            Session = other.Session;
            SetSendTimeoutMs(other.SendTimeoutMs);
            SetMaxResendCnt(other.MaxReSendCnt);
            _diffieHellman = other._diffieHellman;
        }

        ~BasePeer()
        {
            Dispose(false);
        }

        protected override void OnDebug(string format, params object[] args)
        {
            Logger?.Debug(format, args);
        }

        protected override void OnExceededResendCnt(IMessage message)
        {
            Close(CloseReason.LimitExceededReSend);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed) 
                return;
            
            if (disposing)
            {
                PeerIdGenerator.Free(Id);
                Id = PeerId.None;
                _encryptor?.Dispose();
                _encryptor = null;
                ResetMessageProcessor();
            }

            _disposed = true;
        }

        public virtual void Tick()
        {
            if (!IsConnected)
                return;

            foreach (var msg in GetResendCandidates())
            {
                if (msg == null)
                {
                    Close(CloseReason.LimitExceededReSend);
                    return;
                }

                if (!Session.Send(ChannelKind.Reliable, msg))
                    Logger.Warn("Message send failed: {0}({1})", msg.Id, msg.SequenceNumber);
            }
        }
        
        internal void OnPing(double rawRttMs, double avgRttMs, double jitterMs, float packetLossRate)
        {
            RecordRttSample(rawRttMs);
            LatencyAvgMs = avgRttMs;
            LatencyJitterMs = jitterMs;
            PacketLossRate = packetLossRate;
        }
        
        internal void OnMessageAck(long sequenceNumber)
        {
            OnAckReceived(sequenceNumber);
        }
        
        public void Close(CloseReason reason)
        {
            Session.Close(reason);
        }
        
        public IPolicy GetPolicy<TProtocol>() where TProtocol : IProtocol
            => _policyView.Resolve<TProtocol>();
        
        public bool Send<TProtocol>(TProtocol data) where TProtocol : IProtocol
        {
            var channel = data.Channel;
            var policy = GetPolicy<TProtocol>();
            var encryptor = policy.UseEncrypt ? Encryptor : null;
            switch (channel)
            {
                case ChannelKind.Reliable:
                {
                    var msg = new TcpMessage();
                    msg.SetSequenceNumber(GetNextSequenceNumber());
                    msg.Serialize(data, policy, encryptor);
                    
                    if (!IsConnected)
                    {
                        EnqueuePendingSend(msg);
                        return true;
                    }
                    
                    StartSendingMessage(msg);
                    return Session.Send(channel, msg);
                }
                case ChannelKind.Unreliable:
                {
                    var msg = new UdpMessage();
                    msg.SetPeerId(Id);
                    msg.Serialize(data, policy, encryptor);
                    return IsConnected && Session.Send(channel, msg);
                }
                default:
                    throw new Exception($"Unknown channel: {channel}");
            }
        }
        
        internal void ExecuteMessage(IMessage message)
        {
            foreach (var inOrder in ProcessReceivedMessage(message))
                Session.Engine.ExecuteHandler(this, inOrder);
        }

        internal void JoinServer()
        {
            if (Interlocked.CompareExchange(ref _stateCode, PeerStateConst.Authorized, PeerStateConst.NotAuthorized)
                != PeerStateConst.NotAuthorized)
            {
                Logger.Error("The peer has joined the server.");
                return;
            }
                
            OnJoinServer();
        }

        internal void OnSessionAuthed()
        {
            // 정책 생성
            var s = Config.Security;
            var g = new PolicyGlobals(s.UseEncrypt, s.UseCompress, s.CompressionThreshold);
            var newView = new SnapshotPolicyView(g);
            Interlocked.Exchange(ref _policyView, newView);
        }
        
        internal void Online(IClientSession session)
        {
            Interlocked.Exchange(ref _stateCode, PeerStateConst.Online);
            Session = session;
            
            foreach (var msg in DequeuePendingSend())
                session.Send(ChannelKind.Reliable, msg);
            
            OnOnline();
        }

        internal void Offline(CloseReason reason)
        {
            Interlocked.Exchange(ref _stateCode, PeerStateConst.Offline);
            _policyView = new SnapshotPolicyView(PolicyDefaults.Globals);
            ResetSendingMessageState();
            OnOffline(reason);
        }

        internal void LeaveServer(CloseReason reason)
        {
            Interlocked.Exchange(ref _stateCode, PeerStateConst.Closed);
            PeerIdGenerator.Free(Id);
            Id = PeerId.None;
            ResetMessageProcessor();
            OnLeaveServer(reason);
        }

        internal void SetupSecurity(DhKeySize keySize, byte[] publicKey)
        {
            _diffieHellman = new DiffieHellman(keySize);
            _encryptor = new Encryptor(_diffieHellman.DeriveSharedKey(publicKey));
        }

        protected virtual void OnJoinServer()
        {
        }

        protected virtual void OnLeaveServer(CloseReason reason) 
        {
        }

        protected virtual void OnOnline()
        {
        }

        protected virtual void OnOffline(CloseReason reason)
        {
        }

        public override string ToString()
        {
            return $"sessionId={Session.SessionId}, peerId={Id}, peerType={Kind}, remoteEndPoint={RemoteEndPoint}";   
        }
    }
}
