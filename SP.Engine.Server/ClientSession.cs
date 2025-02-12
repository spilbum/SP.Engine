using System;
using System.Net;
using SP.Engine.Common.Logging;
using SP.Engine.Core;
using SP.Engine.Core.Message;
using SP.Engine.Core.Protocol;

namespace SP.Engine.Server
{
    public interface IClientSession : ILoggerProvider
    {
        string SessionId { get; }
        IServer Server { get; }
        IServerConfig Config { get; }
        IPEndPoint LocalEndPoint { get; }
        IPEndPoint RemoteEndPoint { get; }
        void Reject(ERejectReason reason, string detailReason = null);
        bool TrySend(byte[] data);
        void Close(ECloseReason reason);
    }
    
    public class ClientSession<TPeer> : SessionBase<ClientSession<TPeer>>, IProtocolHandler, IClientSession
        where TPeer : PeerBase, IPeer
    {
        public TPeer Peer { get; private set; }
        internal ServerBase<TPeer> Server { get; private set; }
        IServer IClientSession.Server => Server;

        public ERejectReason RejectReason { get; private set; }
        public string RejectDetailReason { get; private set; }
        public bool IsClosing { get; private set; }
        public int AvgLatencyMs { get; private set; }
        public int LatencyStddevMs { get; private set; }
        
        public override void Initialize(ISessionServer server, ISocketSession socketSession)
        {
            base.Initialize(server, socketSession);
            Server = (ServerBase<TPeer>)server;
        }

        public void Reject(ERejectReason reason, string detailReason = null)
        {
            RejectReason = reason;
            RejectDetailReason = detailReason;
            Close(ECloseReason.Rejected);
            Logger.WriteLog(ELogLevel.Debug, "Reject session. reason={0}, detailReason={1}", reason, detailReason);
        }
        
        protected override void OnMessageReceived(IMessage message)
        {
            if (null == message)
                return;
            
            if (message.ProtocolId.IsEngineProtocol())
            {
                // 시스템 메시지
                var invoker = ProtocolManager.GetProtocolInvoker(message.ProtocolId);
                if (null == invoker)
                    throw new InvalidOperationException($"The protocol invoker not found. protocolId={message.ProtocolId}");

                invoker.Invoke(this, message, null);
            }
            else
            {
                var peer = Peer;
                if (null == peer)
                    throw new InvalidOperationException("The peer is null.");

                // 메시지 응답
                SendMessageAck(message.SequenceNumber);
                // 메시지 실행
                peer.ExecuteMessage(message);
            }
        }
        
        private void SendPong(DateTime sentTime)
        {
            var protocol = new EngineProtocolDataS2C.NotifyPongInfo { SentTime = sentTime, ServerTime = DateTime.UtcNow };
            TryInternalSend(protocol);
        }
        
        private void SendMessageAck(long sequenceNumber)
        {
            var protocol = new EngineProtocolDataS2C.NotifyMessageAckInfo { SequenceNumber = sequenceNumber };
            TryInternalSend(protocol);
        }
        
        private void SendCloseHandshake()
        {
            if (!IsClosing)
            {
                IsClosing = true;
                StartClosingTime = DateTime.UtcNow;    
                Server?.EnqueueCloseHandshakePendingQueue(this);
            }
            
            TryInternalSend(new EngineProtocolDataS2C.NotifyClose());
        }

        public override void Close(ECloseReason reason)
        {
            if (reason is ECloseReason.TimeOut || reason is ECloseReason.Rejected)
            {
                SendCloseHandshake();
                return;
            }

            base.Close(reason);
        }

        [ProtocolHandler(EngineProtocolIdC2S.AuthReq)]
        private void OnAuthReq(EngineProtocolDataC2S.AuthReq authReq)
        {
            if (null == Server)
                return;

            var errorCode = ESystemErrorCode.Unknown;
            
            try
            {
                if (!string.IsNullOrEmpty(authReq.SessionId))
                {
                    // 재연결
                    var prevSession = Server.GetSession(authReq.SessionId);
                    if (null != prevSession)
                    {
                        // 이전 세션이 살아 있는 경우
                        if (ERejectReason.None != prevSession.RejectReason)
                            throw new InvalidOperationException($"Reconnection is not allowed because the session was rejected. sessionId={prevSession.SessionId} reason={prevSession.RejectReason}");

                        var peer = prevSession.Peer;

                        // 이전 세션 종료
                        prevSession.Peer = null;
                        prevSession.Close();
                        
                        Peer = peer ?? throw new InvalidOperationException("The peer of previous session is null.");
                        Server.OnlinePeer(peer, this);
                    }
                    else
                    {
                        // 재 연결 대기인 경우
                        var peer = Server.GetWaitingReconnectPeer(authReq.PeerId);
                        Peer = peer ?? throw new InvalidOperationException("The waiting reconnect peer is null.");
                        Server.OnlinePeer(peer, this);
                    }
                }
                else
                {
                    // 최조 연결
                    if (null != Peer)
                        return;

                    var peer = Server.CreatePeer(this, authReq.CryptoKeySize, authReq.CryptoPublicKey);
                    if (null == peer)
                        return;

                    Peer = peer;
                    Peer.OnAuthorized();
                    Server.JoinPeer(Peer);
                }

                IsAuthorized = true;
                errorCode = ESystemErrorCode.Success;
            }
            catch (Exception ex)
            {
                errorCode = ESystemErrorCode.Invalid;
                Logger.WriteLog(ELogLevel.Error, "Failed to authorize peer: exception={0}\r\nstackTrace={1}", ex.Message, ex.StackTrace);
            }
            finally
            {
                var authAck = new EngineProtocolDataS2C.AuthAck { ErrorCode = errorCode };
                if (errorCode == ESystemErrorCode.Success)
                {
                    authAck.SessionId = SessionId;
                    authAck.LimitRequestLength = Config.LimitRequestLength;
                    authAck.SendTimeOutMs = Config.SendTimeOutMs;
                    authAck.MaxReSendCnt = Config.MaxReSendCnt;
                    
                    if (null != Peer)
                    {
                        authAck.PeerId = Peer.PeerId;
                        authAck.CryptoPublicKey = Peer.CryptoPublicKey;
                    }
                }

                TryInternalSend(authAck);
            }
        }

        [ProtocolHandler(EngineProtocolIdC2S.NotifyPingInfo)]
        private void OnNotifyPingInfo(EngineProtocolDataC2S.NotifyPingInfo info)
        {
            SendPong(info.SendTime);
            AvgLatencyMs = info.AvgLatencyMs;
            LatencyStddevMs = info.LatencyStddevMs;
            //Logger.WriteLog(ELogLevel.Debug, $"AvgLatencyMs={info.AvgLatencyMs}, LatencyStddevMs={info.LatencyStddevMs}");
        }

        [ProtocolHandler(EngineProtocolIdC2S.NotifyMessageAckInfo)]
        private void OnNotifyMessageAckInfo(EngineProtocolDataC2S.NotifyMessageAckInfo info)
        {
            Peer?.OnMessageAck(info.SequenceNumber);
        }

        [ProtocolHandler(EngineProtocolIdC2S.NotifyClose)]
        private void OnNotifyClose(EngineProtocolDataC2S.NotifyClose notifyClose)
        {
            Logger.WriteLog(ELogLevel.Debug, "Received a termination request from the client. isClosing={0}", IsClosing);
            if (IsClosing)
            {
                Close(ECloseReason.ClientClosing);
                return;
            }

            IsClosing = true;
            SendCloseHandshake();

            Close(ECloseReason.ClientClosing);
        }   
    }
}
