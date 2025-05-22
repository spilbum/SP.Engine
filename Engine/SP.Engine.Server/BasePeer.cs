using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using SP.Common.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Message;
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
        byte[] DhPublicKey { get; }
        void Send(IProtocolData iProtocolData);
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
                else
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

        public EPeerState State => (EPeerState)_stateCode;
        public EPeerId PeerId { get; private set; }
        public EPeerType PeerType { get; } 
        public ILogger Logger { get; }
        public ISession Session { get; private set; }

        public IPEndPoint LocalEndPoint => Session.LocalEndPoint;
        public IPEndPoint RemoteEndPoint => Session.RemoteEndPoint;
        public bool IsConnected => _stateCode == PeerStateConst.Authorized || _stateCode == PeerStateConst.Online;
        public byte[] DhPublicKey => _diffieHellman.PublicKey;
        public byte[] DhSharedKey => _diffieHellman.SharedKey;
        
        protected BasePeer(EPeerType peerType, ISession session, DhKeySize dhKeySize, byte[] dhPublicKey)
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

        public virtual void Update()
        {
            if (!IsConnected)
                return;

            foreach (var message in GetTimedOutMessages())
            {
                if (!Session.TrySend(message.ToArray()))
                    Logger.Warn("Message send failed: {0}({1})", message.ProtocolId, message.SequenceNumber);
            }

            foreach (var message in GetPendingMessages())
                Send(message);
        }

        protected override void OnMessageSendFailure(IMessage message)
        {
            Logger.Warn("The message resend limit has been exceeded: {0} ({1})", message.ProtocolId, message.SequenceNumber);
            Close(ECloseReason.LimitExceededReSend);
        }

        protected override void OnRttSpikeDetected(long sequenceNumber, double rawRtt, double estimatedRtt)
        {
            Logger.Warn($"RTT spike detected: Seq={sequenceNumber}, Raw={rawRtt:F2}ms, Est={estimatedRtt:F2}ms");
        }

        public void Reject(ERejectReason reason, string detailReason = null)
        {
            Session.Reject(reason, detailReason);
        }

        public void Close(ECloseReason reason)
        {
            Session.Close(reason);
        }
        
        public void Send(IProtocolData protocol)
        {
            try
            {
                var message = new TcpMessage();
                message.Pack(protocol, _diffieHellman.SharedKey, _packOptions);
                Send(message);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }

        private void Send(IMessage message)
        {
            switch (State)
            {
                case EPeerState.Offline:
                    EnqueuePendingMessage(message);
                    break;
                case EPeerState.Authorized:
                case EPeerState.Online:
                    {
                        RegisterSendingMessage(message);

                        var data = message.ToArray();
                        Session.TrySend(data);
                    }
                    break;
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

        internal void Online(ISession session)
        {
            Interlocked.Exchange(ref _stateCode, PeerStateConst.Online);
            Session = session;
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
