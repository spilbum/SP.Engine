using System;
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using SP.Engine.Common.Logging;
using SP.Engine.Core;
using SP.Engine.Core.Networking;
using SP.Engine.Core.Protocols;
using SP.Engine.Core.Security;

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
        byte[] CryptoPublicKey { get; }
        void Send(IProtocolData iProtocolData);
        void Reject(ERejectReason reason, string detailReason = null);
    }
    
    public abstract class PeerBase : MessageProcessor, IProtocolHandler, IPeer, IDisposable
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
        private IClientSession _session;
        private DhSession _dh;

        public EPeerState State => (EPeerState)_stateCode;
        public EPeerId PeerId { get; private set; }
        public EPeerType PeerType { get; } 
        public ILogger Logger { get; }
        public IClientSession Session => _session;
        public IPEndPoint LocalEndPoint => Session.LocalEndPoint;
        public IPEndPoint RemoteEndPoint => Session.RemoteEndPoint;
        public bool IsConnected => _stateCode == PeerStateConst.Authorized || _stateCode == PeerStateConst.Online;
        public byte[] CryptoPublicKey => _dh.PublicKey;
        
        protected PeerBase(EPeerType peerType, IClientSession session, DhKeySize keySize, byte[] clientPublicKey)
        {
            PeerId = PeerIdGenerator.Generate();
            PeerType = peerType;
            Logger = session.Logger;
            SendTimeOutMs = session.Config.SendTimeOutMs;
            MaxReSendCnt = session.Config.MaxReSendCnt;
            _session = session;
            
            _dh = new DhSession(keySize);
            _dh.DeriveSharedKey(clientPublicKey);
        }

        internal void OnAuthorized()
        {
            _stateCode = PeerStateConst.Authorized;
        }

        ~PeerBase()
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

            var reSendMessages = CheckMessageTimeout(out var isLimitExceededReSend);
            if (isLimitExceededReSend)
            {
                Logger.Error("The message resend limit has been exceeded.");
                Close(ECloseReason.LimitExceededReSend);
                return;
            }

            foreach (var message in reSendMessages)
            {
                Session.TrySend(message.ToArray());
            }

            var pending = GetPendingMessages();
            foreach (var message in pending)
                Send(message);
        }

        public void Reject(ERejectReason reason, string detailReason = null)
        {
            Session.Reject(reason, detailReason);
        }

        public void Close(ECloseReason reason)
        {
            Session.Close(reason);
        }
        
        public void Send(IProtocolData data)
        {
            try
            {
                var message = new TcpMessage();
                message.SerializeProtocol(data, data.ProtocolId.IsEngineProtocol() ? null : _dh.SharedKey);
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
                    RegisterPendingMessage(message);
                    break;
                case EPeerState.Authorized:
                case EPeerState.Online:
                    {
                        if (0 == message.SequenceNumber)
                        {
                            // 메시지 상태 등록
                            AddSendingMessage(message);   
                        }

                        var data = message.ToArray();
                        Session.TrySend(data);
                    }
                    break;
            }
        }

        internal void OnMessageAck(long sequenceNumber)
        {
            ReceiveMessageAck(sequenceNumber);
        }
        
        internal void ExecuteMessage(IMessage message)
        {
            var messages = GetPendingReceivedMessages(message);
            foreach (var msg in messages)
            {
                var invoker = ProtocolManager.GetProtocolInvoker(msg.ProtocolId);
                if (null == invoker)
                    throw new InvalidOperationException($"The protocol invoker not found. protocolId={msg.ProtocolId}");

                invoker.Invoke(this, msg, _dh.SharedKey);
            }
        }
        
        internal void JoinServer()
        {
            if (Interlocked.CompareExchange(ref _stateCode, PeerStateConst.Online, PeerStateConst.Authorized)
                != PeerStateConst.Authorized)
            {
                return;
            }
                
            OnJoinServer();
        }

        internal void Online(IClientSession session)
        {
            Interlocked.Exchange(ref _stateCode, PeerStateConst.Online);
            _session = session;
            ResetSendingMessageStates();
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
