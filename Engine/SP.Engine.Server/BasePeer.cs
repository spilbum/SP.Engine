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
        ISession Session { get; }
        bool Send(IProtocol data);
        void Close(CloseReason reason);
    }

    public interface IBasePeer
    {
        DiffieHellman DiffieHellman { get; }
        IBaseSession BaseSession { get; }
    }
    
    public abstract class BasePeer : ReliableMessageProcessor, IPeer, IBasePeer, IHandleContext, IDisposable
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
                if (PeerId.None != peerId)
                    FreePeerIdPool.Enqueue(peerId);
            }
        }

        private int _stateCode = PeerStateConst.NotAuthorized;
        private bool _disposed;
        private DiffieHellman _diffieHellman;
        private AesCbcEncryptor _encryptor;
        private IPolicyView _networkPolicy = new NetworkPolicyView(in PolicyDefaults.Globals);

        public PeerState State => (PeerState)_stateCode;
        public PeerId Id { get; private set; }
        public PeerKind Kind { get; } 
        public ILogger Logger { get; }
        public double LatencyAvgMs { get; private set; }
        public double LatencyJitterMs { get; private set; }
        public float PacketLossRate { get; private set; }
        public IEncryptor Encryptor => _encryptor;
        public ISession Session { get; private set; }
        public IPEndPoint LocalEndPoint => Session.LocalEndPoint;
        public IPEndPoint RemoteEndPoint => Session.RemoteEndPoint;
        public bool IsConnected => _stateCode is PeerStateConst.Authorized or PeerStateConst.Online;
        
        IBaseSession IBasePeer.BaseSession => (IBaseSession)Session;
        DiffieHellman IBasePeer.DiffieHellman => _diffieHellman;
        
        protected BasePeer(PeerKind peerType, ISession session)
        {
            Id = PeerIdGenerator.Generate();
            Kind = peerType;
            Logger = session.Logger;
            SetSendTimeoutMs(session.Config.Network.SendTimeoutMs);
            SetMaxRetryCount(session.Config.Network.MaxRetryCount);
            Session = session;
        }

        protected BasePeer(BasePeer other)
        {
            Id = other.Id;
            Kind = other.Kind;
            Logger = other.Logger;
            Session = other.Session;
            SetSendTimeoutMs(other.SendTimeoutMs);
            SetMaxRetryCount(other.MaxRetryCount);
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

        protected override void OnRetryLimitExceeded(IMessage message)
        {
            Close(CloseReason.LimitExceededResend);
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
                ResetProcessorState();
            }

            _disposed = true;
        }

        public virtual void Tick()
        {
            if (!IsConnected)
                return;

            foreach (var msg in FindExpiredForRetry())
            {
                if (!Session.TrySend(ChannelKind.Reliable, msg))
                    Logger.Warn("Message send failed: {0}({1})", msg.Id, msg.SequenceNumber);
            }
        }
        
        internal void OnPing(double rawRttMs, double avgRttMs, double jitterMs, float packetLossRate)
        {
            AddRttSample(rawRttMs);
            LatencyAvgMs = avgRttMs;
            LatencyJitterMs = jitterMs;
            PacketLossRate = packetLossRate;
        }
        
        internal void OnMessageAck(long sequenceNumber)
        {
            RemoveMessageState(sequenceNumber);
        }
        
        public void Close(CloseReason reason)
        {
            Session.Close(reason);
        }
        
        public IPolicy GetNetworkPolicy(Type protocolType)
            => _networkPolicy.Resolve(protocolType);
        
        public bool Send(IProtocol data)
        {
            var channel = data.Channel;
            var policy = GetNetworkPolicy(data.GetType());
            var encryptor = policy.UseEncrypt ? Encryptor : null;
            switch (channel)
            {
                case ChannelKind.Reliable:
                {
                    var msg = new TcpMessage();
                    msg.SetSequenceNumber(GetNextReliableSeq());
                    msg.Serialize(data, policy, encryptor);

                    if (!IsConnected)
                    {
                        EnqueuePendingMessage(msg);
                        return true;
                    }
                    
                    RegisterMessageState(msg);
                    return Session.TrySend(channel, msg);
                }
                case ChannelKind.Unreliable:
                {
                    var msg = new UdpMessage();
                    msg.SetPeerId(Id);
                    msg.Serialize(data, policy, encryptor);
                    return Session.TrySend(channel, msg);
                }
                default:
                    throw new Exception($"Unknown channel: {channel}");
            }
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
            var n = Session.Config.Network;
            var g = new PolicyGlobals(n.UseEncrypt, n.UseCompress, n.CompressionThreshold);
            var newView = new NetworkPolicyView(g);
            Interlocked.Exchange(ref _networkPolicy, newView);
        }
        
        internal void Online(ISession session)
        {
            Interlocked.Exchange(ref _stateCode, PeerStateConst.Online);
            Session = session;
            
            foreach (var msg in DequeuePendingMessages())
                session.TrySend(ChannelKind.Reliable, msg);
            
            OnOnline();
        }

        internal void Offline(CloseReason reason)
        {
            Interlocked.Exchange(ref _stateCode, PeerStateConst.Offline);
            _networkPolicy = new NetworkPolicyView(PolicyDefaults.Globals);
            ResetAllMessageStates();
            OnOffline(reason);
        }

        internal void LeaveServer(CloseReason reason)
        {
            Interlocked.Exchange(ref _stateCode, PeerStateConst.Closed);
            PeerIdGenerator.Free(Id);
            Id = PeerId.None;
            ResetProcessorState();
            OnLeaveServer(reason);
        }

        internal void CreateEncryptor(DhKeySize keySize, byte[] otherPublicKey)
        {
            var dh = new DiffieHellman(keySize);
            _encryptor = new AesCbcEncryptor(dh.DeriveSharedKey(otherPublicKey));
            _diffieHellman = dh;
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
