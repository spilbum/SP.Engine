using System;
using System.Net;
using SP.Engine.Runtime;
using SP.Engine.Protocol;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Command;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server
{
    public interface ISession : ICommandContext
    {
        string Id { get; }
        IPEndPoint LocalEndPoint { get; }
        IPEndPoint RemoteEndPoint { get; }
        IEngineConfig Config { get; }
        IEngine Engine { get; }
        bool TrySend(ChannelKind channel, IMessage message);
        void Close(CloseReason reason);
    }
    
    public sealed class Session : BaseSession, ISession
    {
        private Engine _engine;
        IEngine ISession.Engine => _engine;
        
        public BasePeer Peer { get; private set; }
        public bool IsClosing { get; private set; }

        public override void Initialize(IBaseEngine engine, TcpNetworkSession networkSession)
        {
            base.Initialize(engine, networkSession);
            _engine = (Engine)engine;
        }

        internal void OnSessionHandshake(C2SEngineProtocolData.SessionAuthReq data)
        {
            var ack = new S2CEngineProtocolData.SessionAuthAck { Result = SessionHandshakeResult.None };
            var engine = _engine;
            var peer = Peer;

            try
            {
                if (string.IsNullOrEmpty(data.SessionId))
                {
                    // 최초 연결
                    if (null != peer)
                        throw new SessionAuthException(SessionHandshakeResult.InvalidRequest, 
                            $"Already created peer. sessionId={peer.Session.Id}, peerId={peer.Id}");

                    if (!engine.CreatePeer(this, out var p) || p is not BasePeer basePeer)
                           throw new SessionAuthException(SessionHandshakeResult.InternalError, "Failed to create peer.");
                    
                    peer = basePeer;
                    
                    if (!peer.TryKeyExchange(data.KeySize, data.ClientPublicKey))
                        throw new SessionAuthException(SessionHandshakeResult.KeyExchangeFailed, "Key exchange failed.");
                    
                    engine.JoinPeer(peer);
                }
                else
                {
                    // 재연결
                    var prevSession = (Session)engine.GetSession(data.SessionId);
                    if (null != prevSession)
                    {
                        // 이전 세션이 살아 있는 경우
                        if (CloseReason.Rejected == prevSession.CloseReason)
                            throw new SessionAuthException(SessionHandshakeResult.ReconnectionNotAllowed,
                                $"Reconnection is not allowed because the session was rejected. sessionId={prevSession.Id}");

                        peer = prevSession.Peer;
                        prevSession.Close();
                    }
                    else
                    {
                        // 재 연결 대기인 경우
                        peer = engine.GetWaitingReconnectPeer(data.PeerId);
                        if (peer == null)
                            throw new SessionAuthException(SessionHandshakeResult.SessionNotFound,
                                $"No waiting reconnection peer found for sessionId={data.SessionId}");
                    }

                    engine.OnlinePeer(peer, this);
                }

                Peer = peer ?? throw new InvalidOperationException("peer is null.");
                Peer.OnSessionAuthed();
                IsAuthorized = true;
                ack.Result = SessionHandshakeResult.Ok;
                Logger.Debug("Session authentication succeeded. sessionId={0}, peerId={1}", Id, peer.Id);
            }
            catch (SessionAuthException e)
            {
                ack.Result = e.Result;
                #if DEBUG
                ack.Reason = e.Message;
                #endif
                Logger.Error("Session authentication failed ({0}). {1}\r\nstackTrace={2}",
                    e.Result, e.Message, e.StackTrace);
            }
            catch (Exception e)
            {
                ack.Result = SessionHandshakeResult.InternalError;
                Logger.Error("Session authentication failed. {0}\r\nstackTrace={1}",
                    e.Message, e.StackTrace);
            }
            finally
            {
                if (ack.Result  == SessionHandshakeResult.Ok)
                {
                    var network = Config.Network;
                    ack.SessionId = Id;
                    ack.MaxFrameBytes = network.MaxFrameBytes;
                    ack.SendTimeoutMs = network.SendTimeoutMs;
                    ack.MaxRetryCount = network.MaxRetryCount;
                    ack.UdpOpenPort = engine.GetOpenPort(SocketMode.Udp);

                    if (peer != null)
                    {
                        ack.PeerId = peer.Id;
                        
                        if (network.UseEncrypt)
                        {
                            ack.UseEncrypt = true;
                            ack.ServerPublicKey = peer.LocalPublicKey;
                        }

                        if (network.UseCompress)
                        {
                            ack.UseCompress = true;
                            ack.CompressionThreshold = network.CompressionThreshold;
                        }
                    }
                }
                
                if (!InternalSend(ack))
                    Logger.Error("Failed to send session auth ack. sessionId={0}", Id);
            }
        }

        internal void OnUdpHandshake(C2SEngineProtocolData.UdpHelloReq data)
        {
            var ack = new S2CEngineProtocolData.UdpHelloAck { Result = UdpHandshakeResult.None };
            
            try
            {
                if (Id != data.SessionId || Peer == null || Peer.Id != data.PeerId)
                {
                    ack.Result = UdpHandshakeResult.InvalidRequest;
                    Logger.Debug("UDP hello invalid ids. sid={0}/{1}, pid={2}/{3}",
                        data.SessionId, Id, data.PeerId, Peer?.Id);
                    return;
                }

                if (IsClosing || IsClosed)
                {
                    ack.Result = UdpHandshakeResult.InvalidRequest;
                    Logger.Debug("UDP hello while closing. sid={0}", Id);
                    return;
                }

                if (data.Mtu <= 0)
                {
                    ack.Result = UdpHandshakeResult.InvalidRequest;
                    Logger.Debug("UDP hello invalid mtu. mtu={0}", data.Mtu);
                    return;
                }
                
                const ushort minIpv4Mtu = 576;
                
                var net = Config.Network;
                var minMtu = Math.Max(minIpv4Mtu, net.MinMtu);
                var maxMtu = Math.Max(minMtu, net.MaxMtu);
                var negotiated = (ushort)Math.Min(Math.Max(data.Mtu, minMtu), maxMtu);
                UdpSocket.SetMaxFrameSize(negotiated);
                ack.Mtu = negotiated;
                
                Logger.Debug("UDP hello OK - pid={0}, sid={1}, mtu={2}", Peer.Id, Id, negotiated);
                ack.Result = UdpHandshakeResult.Ok;
            }
            catch (Exception e)
            {
                ack.Result = UdpHandshakeResult.InternalError;
                Logger.Error("Udp handshake failed. {0}", e.Message);
            }
            finally
            {
                if (!InternalSend(ack))
                    Logger.Error("Failed to send UDP hello ack. sessionId={0}", Id);
            }
        }
        
        private bool InternalSend(IProtocolData data)
        {
            var channel = data.Channel;
            var policy = Peer.GetNetworkPolicy(data.GetType());
            var encryptor = policy.UseEncrypt ? Peer.Encryptor : null;
            var compressor = policy.UseCompress ? Peer.Compressor : null;
            switch (channel)
            {
                case ChannelKind.Reliable:
                {
                    var msg = new TcpMessage();
                    msg.SetSequenceNumber(0);
                    msg.Serialize(data, policy, encryptor, compressor);
                    return TrySend(channel, msg);
                }
                case ChannelKind.Unreliable:
                {
                    var msg = new UdpMessage();              
                    msg.SetPeerId(Peer.Id);
                    msg.Serialize(data, policy, encryptor, compressor);
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
        
        TProtocol ICommandContext.Deserialize<TProtocol>(IMessage message)
            => message.Deserialize<TProtocol>(Peer?.Encryptor, Peer?.Compressor);
    }
}
