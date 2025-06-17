using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using SP.Common.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;
using SP.Engine.Server.ProtocolHandler;

namespace SP.Engine.Server
{
    public enum EPeerType : byte
    {
        None = 0,
        User,
        Server,
    }
    
    public enum EPeerState
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
        EPeerId PeerId { get; }
        EPeerType PeerType { get; }
        EPeerState State { get; }
        IClientSession Session { get; }
        UdpFragmentAssembler Assembler { get; }
        ushort UdpMtu { get; }
        byte[] DhPublicKey { get; }
        bool Send(IProtocolData data);
        void Reject(ERejectReason reason, string detailReason = null);
    }
    
    public abstract class BasePeer : MessageProcessor, IPeer, IHandleContext, IDisposable
    {
        private static class PeerIdGenerator
        {
            private static readonly ConcurrentQueue<EPeerId> FreePeerIdPool = new ConcurrentQueue<EPeerId>();
            private static int _latestPeerId;

            public static EPeerId Generate()
            {
                if (_latestPeerId >= int.MaxValue)
                    throw new InvalidOperationException("ID overflow detected");

                if (FreePeerIdPool.TryDequeue(out var peerId))
                    return peerId;
                return (EPeerId)Interlocked.Increment(ref _latestPeerId);
            }

            public static void Free(EPeerId peerId)
            {
                if (EPeerId.None != peerId)
                    FreePeerIdPool.Enqueue(peerId);
            }
        }

        private int _stateCode = PeerStateConst.NotAuthorized;
        private bool _disposed;
        private readonly DiffieHellman _diffieHellman;
        private readonly PackOptions _packOptions;

        public UdpFragmentAssembler Assembler { get; } = new();
        public EPeerState State => (EPeerState)_stateCode;
        public IClientSession Session { get; private set; }
        public EPeerId PeerId { get; private set; }
        public EPeerType PeerType { get; } 
        public ILogger Logger { get; }
        public ushort UdpMtu { get; private set; }
        public double LatencyAvg { get; private set; }
        public double LatencyStdDev { get; private set; }
        
        public IPEndPoint LocalEndPoint => Session.LocalEndPoint;
        public IPEndPoint RemoteEndPoint => Session.RemoteEndPoint;
        public bool IsConnected => _stateCode == PeerStateConst.Authorized || _stateCode == PeerStateConst.Online;
        public byte[] DhPublicKey => _diffieHellman.PublicKey;
        public byte[] DhSharedKey => _diffieHellman.SharedKey;
        
        protected BasePeer(EPeerType peerType, IClientSession session, DhKeySize dhKeySize, byte[] dhPublicKey)
        {
            PeerId = PeerIdGenerator.Generate();
            PeerType = peerType;
            Logger = session.Logger;
            SetSendTimeOutMs(session.Config.SendTimeOutMs);
            SetMaxReSendCnt(session.Config.MaxReSendCnt);
            Session = session;
            _packOptions = session.Config.ToPackOptions();
            _diffieHellman = new DiffieHellman(dhKeySize);
            _diffieHellman.DeriveSharedKey(dhPublicKey);
        }

        protected BasePeer(BasePeer other)
        {
            PeerId = other.PeerId;
            PeerType = other.PeerType;
            Logger = other.Logger;
            Session = other.Session;
            SetSendTimeOutMs(other.SendTimeOutMs);
            SetMaxReSendCnt(other.MaxReSendCnt);
            _diffieHellman = other._diffieHellman;
        }

        ~BasePeer()
        {
            Dispose(false);
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
                PeerIdGenerator.Free(PeerId);
                PeerId = EPeerId.None;
                
                ResetMessageProcessor();
            }

            _disposed = true;
        }

        public virtual void Tick()
        {
            if (!IsConnected)
                return;

            foreach (var message in GetResendableMessages())
            {
                if (!Session.Send(message))
                    Logger.Warn("Message send failed: {0}({1})", message.ProtocolId, message.SequenceNumber);
            }

            foreach (var message in GetPendingMessages())
                Session.Send(message);
            
            Assembler.Cleanup(TimeSpan.FromSeconds(10));
        }
        
        internal void OnPing(double latencyAvg, double latencyStdDev)
        {
            LatencyAvg = latencyAvg;
            LatencyStdDev = latencyStdDev;
        }
        
        internal void OnMessageAck(long sequenceNumber)
        {
            OnAckReceived(sequenceNumber);
        }
        
        protected override void OnMessageSendFailure(IMessage message)
        {
            Logger.Warn("The message resend limit has been exceeded: {0} ({1})", message.ProtocolId, message.SequenceNumber);
            Close(ECloseReason.LimitExceededReSend);
        }

        public void Reject(ERejectReason reason, string detailReason = null)
        {
            Session.Reject(reason, detailReason);
        }

        public void Close(ECloseReason reason)
        {
            Session.Close(reason);
        }
        
        public bool Send(IProtocolData data)
        {
            var transport = TransportHelper.Resolve(data);
            switch (transport)
            {
                case ETransport.Tcp:
                {
                    var message = new TcpMessage();
                    message.SetSequenceNumber(GetNextSendSequenceNumber());
                    message.Pack(data, _diffieHellman.SharedKey, _packOptions);
                    return Send(message);
                }
                case ETransport.Udp:
                {
                    var message = new UdpMessage();
                    message.SetPeerId(PeerId);
                    message.Pack(data, _diffieHellman.SharedKey, _packOptions);
                    return Session.Send(message);
                }
                default:
                    throw new Exception($"Unknown transport: {transport}");
            }
        }
        
        private bool Send(IMessage message)
        {
            if (!IsConnected)
                EnqueuePendingMessage(message);
            else
            {
                RegisterSendingMessage(message);
                if (!Session.Send(message))
                    return false;
            }
            
            return true;
        }

        internal void ExecuteMessage(IMessage message)
        {
            foreach (var ordered in DrainInOrderMessages(message))
                Session.Engine.ExecuteHandler(this, ordered);
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

        internal void Online(IClientSession clientSession)
        {
            Interlocked.Exchange(ref _stateCode, PeerStateConst.Online);
            Session = clientSession;
            ResetAllSendingStates();
            OnOnline();
        }

        internal void Offline(ECloseReason reason)
        {
            Interlocked.Exchange(ref _stateCode, PeerStateConst.Offline);
            OnOffline(reason);
        }

        internal void LeaveServer(ECloseReason reason)
        {
            Interlocked.Exchange(ref _stateCode, PeerStateConst.Closed);
            PeerIdGenerator.Free(PeerId);
            PeerId = EPeerId.None;
            OnLeaveServer(reason);
        }

        internal void SetUdpMtu(ushort udpMtu)
        {
            UdpMtu = udpMtu;
        }

        protected virtual void OnJoinServer()
        {
        }

        protected virtual void OnLeaveServer(ECloseReason reason) 
        {
        }

        protected virtual void OnOnline()
        {
        }

        protected virtual void OnOffline(ECloseReason reason)
        {
        }

        public override string ToString()
        {
            return $"sessionId={Session.SessionId}, peerId={PeerId}, peerType={PeerType}, remoteEndPoint={RemoteEndPoint}";   
        }
    }
}
