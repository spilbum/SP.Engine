using System;
using SP.Engine.Runtime;
using SP.Engine.Protocol;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server
{
    public interface IClientSession<out TPeer> : IClientSession
        where TPeer : BasePeer, IPeer
    {
        TPeer Peer { get; }
    }
    
    public sealed class ClientSession<TPeer> : BaseClientSession<ClientSession<TPeer>>, IClientSession<TPeer>
        where TPeer : BasePeer, IPeer
    {
        public TPeer Peer { get; private set; }
        public bool IsClosing { get; private set; }
        private Engine<TPeer> Engine { get; set; }
        
        public override void Initialize(IEngine engine, TcpNetworkSession networkSession)
        {
            base.Initialize(engine, networkSession);
            Engine = (Engine<TPeer>)engine;
        }

        internal void OnSessionAuth(C2SEngineProtocolData.SessionAuthReq data)
        {
            var errorCode = EngineErrorCode.Unknown;
            var engine = Engine;
            var peer = Peer;

            try
            {
                if (string.IsNullOrEmpty(data.SessionId))
                {
                    // 최조 연결
                    if (null != peer)
                        return;

                    peer = engine.CreateNewPeer(this, data.KeySize, data.ClientPublicKey);
                    if (null == peer)
                        return;

                    engine.JoinPeer(peer);
                }
                else
                {
                    // 재연결
                    var prevSession = (ClientSession<TPeer>)engine.GetSession(data.SessionId);
                    if (null != prevSession)
                    {
                        // 이전 세션이 살아 있는 경우
                        if (CloseReason.Rejected == prevSession.CloseReason)
                            throw new ErrorCodeException(EngineErrorCode.ReconnectionFailed,
                                $"Reconnection is not allowed because the session was rejected. sessionId={prevSession.SessionId}");

                        peer = prevSession.Peer;
                        prevSession.Close();
                    }
                    else
                    {
                        // 재 연결 대기인 경우
                        peer = engine.GetWaitingReconnectPeer(data.PeerId);
                        if (peer == null)
                            throw new ErrorCodeException(EngineErrorCode.ReconnectionFailed,
                                $"No waiting reconnection peer found for sessionId={data.SessionId}");
                    }

                    engine.OnlinePeer(peer, this);
                }

                Peer = peer ?? throw new InvalidOperationException("peer is null.");
                Peer.OnSessionAuthed();
                Encryptor = Peer.Encryptor;
                IsAuthorized = true;
                errorCode = EngineErrorCode.Success;
                Logger.Debug("Session authentication succeeded. sessionId={0}, peerId={1}", SessionId, peer.Id);
            }
            catch (ErrorCodeException e)
            {
                errorCode = e.ErrorCode;
                Logger.Error("Session authentication failed ({0}). {1}\r\nstackTrace={2}",
                    e.ErrorCode, e.Message, e.StackTrace);
            }
            catch (Exception e)
            {
                errorCode = EngineErrorCode.Invalid;
                Logger.Error("Session authentication failed. {0}\r\nstackTrace={1}",
                    e.Message, e.StackTrace);
            }
            finally
            {
                var authAck = new S2CEngineProtocolData.SessionAuthAck { ErrorCode = errorCode };
                if (errorCode == EngineErrorCode.Success)
                {
                    authAck.SessionId = SessionId;
                    authAck.MaxFrameBytes = Config.Network.MaxFrameBytes;
                    authAck.SendTimeoutMs = Config.Network.SendTimeoutMs;
                    authAck.MaxResendCount = Config.Session.MaxResendCount;
                    authAck.UdpOpenPort = engine.GetOpenPort(ESocketMode.Udp);

                    if (peer != null)
                    {
                        authAck.PeerId = peer.Id;
                        
                        var security = Config.Security;
                        if (security.UseEncrypt)
                        {
                            authAck.UseEncrypt = true;
                            authAck.ServerPublicKey = peer.DhPublicKey;
                        }

                        if (security.UseCompress)
                        {
                            authAck.UseCompress = true;
                            authAck.CompressionThreshold = security.CompressionThreshold;
                        }
                    }
                }
                
                if (!SendEngine(authAck))
                    Logger.Error("Failed to send session auth ack. sessionId={0}", SessionId);
            }
        }

        protected override void ExecuteMessage(IMessage message)
        {
            Engine.ExecuteMessage(this, message);
        }
        
        internal bool SendEngine<TProtocol>(TProtocol data) where TProtocol : IProtocol
        {
            var channel = data.Channel;
            var policy = Peer.GetPolicy<TProtocol>();
            var encryptor = policy.UseEncrypt ? Encryptor : null;
            switch (channel)
            {
                case ChannelKind.Reliable:
                {
                    var msg = new TcpMessage();
                    msg.SetSequenceNumber(0);
                    msg.Serialize(data, policy, encryptor);
                    return Send(channel, msg);
                }
                case ChannelKind.Unreliable:
                {
                    var msg = new UdpMessage();              
                    msg.SetPeerId(Peer.Id);
                    msg.Serialize(data, policy, encryptor);
                    return Send(channel, msg);
                }
                default:
                    throw new Exception($"Unknown channel: {channel}");
            }
        }
        
        internal void SendPong(long sendTimeMs)
        {
            SendEngine(new S2CEngineProtocolData.Pong
            {
                SendTimeMs = sendTimeMs, 
                ServerTimeMs = Engine.GetServerTimeMs()
            });
        }
        
        internal void SendCloseHandshake()
        {
            if (!IsClosing)
            {
                IsClosing = true;
                StartClosingTime = DateTime.UtcNow;    
                Engine?.EnqueueCloseHandshakePending(this);
            }

            var close = new S2CEngineProtocolData.Close();
            SendEngine(close);
        }
        
        internal void SendMessageAck(long sequenceNumber)
        {
            var messageAck = new S2CEngineProtocolData.MessageAck { SequenceNumber = sequenceNumber };
            SendEngine(messageAck);
        }
        
        public override void Close()
        {
            Peer = null;
            base.Close();
        }

        public override void Close(CloseReason reason)
        {
            if (reason is CloseReason.TimeOut or CloseReason.Rejected)
            {
                SendCloseHandshake();
                return;
            }

            base.Close(reason);
        }
    }
}
