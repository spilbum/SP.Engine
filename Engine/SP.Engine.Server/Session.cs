using System;
using System.Net;
using SP.Common.Logging;
using SP.Engine.Runtime;
using SP.Engine.Protocol;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.ProtocolHandler;

namespace SP.Engine.Server
{
    public interface ISession : ILogContext, IHandleContext
    {
        string SessionId { get; }
        IPEndPoint LocalEndPoint { get; }
        IPEndPoint RemoteEndPoint { get; }
        IEngineConfig Config { get; }
        IEngine Engine { get; }
        bool TrySend(ChannelKind channel, IMessage message);
        void Close(CloseReason reason);
    }
    
    public sealed class Session<TPeer> : BaseSession<Session<TPeer>>, ISession
        where TPeer : BasePeer, IPeer
    {
        private Engine<TPeer> _engine;
        IEngine ISession.Engine => _engine;
        
        public TPeer Peer { get; private set; }
        public bool IsClosing { get; private set; }
        
        public override void Initialize(IBaseEngine engine, TcpNetworkSession networkSession)
        {
            base.Initialize(engine, networkSession);
            _engine = (Engine<TPeer>)engine;
        }

        internal void OnSessionAuth(C2SEngineProtocolData.SessionAuthReq data)
        {
            var errorCode = EngineErrorCode.Unknown;
            var engine = _engine;
            var peer = Peer;

            try
            {
                if (string.IsNullOrEmpty(data.SessionId))
                {
                    // 최조 연결
                    if (null != peer)
                        return;

                    peer = engine.CreatePeer(this);
                    if (null == peer)
                        return;

                    peer.CreateEncryptor(data.KeySize, data.ClientPublicKey);
                    engine.JoinPeer(peer);
                }
                else
                {
                    // 재연결
                    var prevSession = (Session<TPeer>)engine.GetSession(data.SessionId);
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
                    var network = Config.Network;
                    authAck.SessionId = SessionId;
                    authAck.MaxFrameBytes = network.MaxFrameBytes;
                    authAck.SendTimeoutMs = network.SendTimeoutMs;
                    authAck.MaxRetryCount = network.MaxRetryCount;
                    authAck.UdpOpenPort = engine.GetOpenPort(ESocketMode.Udp);

                    if (peer != null)
                    {
                        authAck.PeerId = peer.Id;
                        
                        if (network.UseEncrypt)
                        {
                            authAck.UseEncrypt = true;
                            authAck.ServerPublicKey = ((IBasePeer)peer).DiffieHellman.PublicKey;
                        }

                        if (network.UseCompress)
                        {
                            authAck.UseCompress = true;
                            authAck.CompressionThreshold = network.CompressionThreshold;
                        }
                    }
                }
                
                if (!InternalSend(authAck))
                    Logger.Error("Failed to send session auth ack. sessionId={0}", SessionId);
            }
        }
        
        internal bool InternalSend(IProtocol data)
        {
            var channel = data.Channel;
            var policy = Peer.GetNetworkPolicy(data.GetType());
            var encryptor = policy.UseEncrypt ? Encryptor : null;
            switch (channel)
            {
                case ChannelKind.Reliable:
                {
                    var msg = new TcpMessage();
                    msg.SetSequenceNumber(0);
                    msg.Serialize(data, policy, encryptor);
                    return TrySend(channel, msg);
                }
                case ChannelKind.Unreliable:
                {
                    var msg = new UdpMessage();              
                    msg.SetPeerId(Peer.Id);
                    msg.Serialize(data, policy, encryptor);
                    return TrySend(channel, msg);
                }
                default:
                    throw new Exception($"Unknown channel: {channel}");
            }
        }
        
        internal void SendPong(long sendTimeMs)
        {
            InternalSend(new S2CEngineProtocolData.Pong
            {
                SendTimeMs = sendTimeMs, 
                ServerTimeMs = _engine.GetServerTimeMs()
            });
        }
        
        internal void SendCloseHandshake()
        {
            if (!IsClosing)
            {
                IsClosing = true;
                StartClosingTime = DateTime.UtcNow;    
                _engine.EnqueueCloseHandshakePending(this);
            }

            var close = new S2CEngineProtocolData.Close();
            InternalSend(close);
        }
        
        internal void SendMessageAck(long sequenceNumber)
        {
            var messageAck = new S2CEngineProtocolData.MessageAck { SequenceNumber = sequenceNumber };
            InternalSend(messageAck);
        }
        
        protected override void ExecuteMessage(IMessage message)
        {
            _engine.ExecuteMessage(this, message);
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
