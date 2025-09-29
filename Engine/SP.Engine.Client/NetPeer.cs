using System;
using System.Net;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SP.Common.Buffer;
using SP.Common.Logging;
using SP.Engine.Client.Configuration;
using SP.Engine.Client.ProtocolHandler;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Client
{
    public class MessageReceivedEventArgs : EventArgs
    {
        public MessageReceivedEventArgs(IMessage message)
        {
            Message = message;
        }

        public IMessage Message { get; private set; }
    }

    public class StateChangedEventArgs : EventArgs
    {
        public ENetPeerState OldState { get; private set; }
        public ENetPeerState NewState { get; private set; }

        public StateChangedEventArgs(ENetPeerState oldState, ENetPeerState newState)
        {
            OldState = oldState;
            NewState = newState;
        }
    }

    public enum ENetPeerState
    {
        None = 0,
        /// <summary>
        /// 최초 연결 시도
        /// </summary>
        Connecting = 1,
        /// <summary>
        /// 인증 처리중
        /// </summary>
        Handshake = 2,
        /// <summary>
        /// 연결 유지중
        /// </summary>
        Open = 3,
        /// <summary>
        /// 재연결 시도
        /// </summary>
        Reconnecting = 4,
        /// <summary>
        /// 종료중
        /// </summary>
        Closing = 5,
        /// <summary>
        /// 종료됨
        /// </summary>
        Closed = 6
    }

    public sealed class NetPeer : ReliableMessageProcessor, IDisposable
    {
        private string _sessionId;
        private int _stateCode;
        private bool _disposed;
        private TcpNetworkSession _session;
        private UdpSocket _udpSocket;
        private TickTimer _tickTimer;
        private AesCbcEncryptor _encryptor;
        private readonly BinaryBuffer _receiveBuffer = new BinaryBuffer();
        private readonly ConcurrentQueue<IMessage> _sendingMessageQueue = new ConcurrentQueue<IMessage>();
        private readonly ConcurrentQueue<IMessage> _receivedMessageQueue = new ConcurrentQueue<IMessage>();
        private readonly Dictionary<ushort, IHandler<NetPeer, IMessage>> _engineHandlers =
            new Dictionary<ushort, IHandler<NetPeer, IMessage>>();
        private double _serverTimeOffsetMs;
        private TickTimer _udpKeepAliveTimer;
        private readonly DiffieHellman _diffieHellman = new DiffieHellman(DhKeySize.Bit2048);
        private readonly ChannelRouter _channelRouter = new ChannelRouter();
        private IPolicyView _policyView = new SnapshotPolicyView(in PolicyDefaults.Globals);
        
        public int ConnectAttempts { get; private set; }
        public int ReconnectAttempts { get; private set; }
        public long LastSendPingTime { get; private set; }
        public EndPoint RemoteEndPoint { get; private set; }
        public PeerId PeerId { get; private set; }
        public ILogger Logger { get; }
        public EngineConfig Config { get; }
        public int MaxFrameBytes { get; private set; }
        public LatencyStats LatencyStats { get; }
        public ENetPeerState State => (ENetPeerState)_stateCode;
        public bool IsConnected => State == ENetPeerState.Open;
        public IEncryptor Encryptor => _encryptor;

        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler Offline;
        public event EventHandler<ErrorEventArgs> Error;
        public event EventHandler<MessageReceivedEventArgs> MessageReceived;
        public event EventHandler<StateChangedEventArgs> StateChanged;
        public event EventHandler UdpOpened;
        public event EventHandler UdpClosed;
        
        public NetPeer(EngineConfig config, ILogger logger)
        {
            Config = config;
            Logger = logger;
            MaxFrameBytes = 32 * 1024;
            LatencyStats = new LatencyStats(config.LatencySampleWindowSize);
            
            _engineHandlers[S2CEngineProtocolId.SessionAuthAck] = new SessionAuth();
            _engineHandlers[S2CEngineProtocolId.Close] = new Close();
            _engineHandlers[S2CEngineProtocolId.MessageAck] = new MessageAck();
            _engineHandlers[S2CEngineProtocolId.Pong] = new Pong();
            _engineHandlers[S2CEngineProtocolId.UdpHelloAck] = new UdpHelloAck();
        }

        ~NetPeer()
        {
            Dispose(false);
        }

        public (int totalSentBytes, int totalReceivedBytes) GetTcpTrafficInfo()
        {
            var tcp = _session?.GetTraffic();
            return (
                tcp?.totalSentBytes ?? 0,
                tcp?.totalReceivedBytes ?? 0
            );
        }

        public (int totalSentBytes, int totalReceivedBytes) GetUdpTrafficInfo()
        {
            var udp = _udpSocket?.GetTraffic();
            return (
                udp?.totalSentBytes ?? 0,
                udp?.totalReceivedBytes ?? 0
            );
        }

        protected override void OnDebug(string format, params object[] args)
        {
            Logger?.Debug(format, args);
        }

        protected override void OnExceededResendCnt(IMessage message)
        {
            Logger?.Error("Message {0}({1}) exceeded max resend count.", message.SequenceNumber, message.Id);
            // 재전송 횟수 초가로 종료함
            Close();
        }

        private static long GetCurrentTimeMs()
            => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

        public DateTime GetServerTime()
        {
            var estimateMs = GetCurrentTimeMs() + (long)_serverTimeOffsetMs;
            return DateTimeOffset.FromUnixTimeMilliseconds(estimateMs).UtcDateTime;
        }

        public void Connect(string ip, int port)
        {
            if (null != _session)
                throw new InvalidOperationException("Already opened");

            if (string.IsNullOrEmpty(ip) || 0 >= port)
                throw new ArgumentException("Invalid ip or port");

            RemoteEndPoint = ResolveEndPoint(ip, port);
            StartConnection();
        }
        
        private static EndPoint ResolveEndPoint(string ip, int port)
        {
            if (IPAddress.TryParse(ip, out var address))
                return new IPEndPoint(address, port);
            return new DnsEndPoint(ip, port);
        }

        public void Tick()
        {
            _tickTimer?.Tick();
            
            _udpKeepAliveTimer?.Tick();
            _udpSocket?.Assembler.Cleanup(TimeSpan.FromSeconds(10));
            
            // 수신된 메시지 처리
            DequeueReceivedMessage();

            // 전송 메시지 처리
            DequeueSendingMessage();

            // 대기 중인 메시지 전송
            DequeuePendingMessage();

            // 재 전송 메시지 체크
            CheckReSendMessages();
        }

        private void DequeuePendingMessage()
        {
            if (!IsConnected)
                return;

            foreach (var msg in DequeuePendingSend())
                EnqueueSendingMessage(msg);   
        }
        
        public bool Send(IProtocol data)
        {
            var sequenceNumber = data.Channel == ChannelKind.Reliable ? GetNextReliableSeq() : 0;
            var policy = _policyView.Resolve(data.GetType());
            var encryptor = policy.UseEncrypt ? Encryptor : null;
            return TrySend(sequenceNumber, data, policy, encryptor);
        }

        private bool InternalSend(IProtocol data)
        { 
            var policy = _policyView.Resolve(data.GetType());
            var encryptor = policy.UseEncrypt ? Encryptor : null;
            return TrySend(0, data, policy, encryptor);
        }
        
        private bool TrySend(long sequenceNumber, IProtocol data, IPolicy policy, IEncryptor encryptor)
        {
            var channel = data.Channel;

            try
            {
                switch (channel)
                {
                    case ChannelKind.Reliable:
                    {
                        var msg = new TcpMessage();
                        msg.SetSequenceNumber(sequenceNumber);
                        msg.Serialize(data, policy, encryptor);
                    
                        if (sequenceNumber > 0 && !IsConnected)
                        {
                            EnqueuePendingSend(msg);
                            return true;
                        }
            
                        if (sequenceNumber > 0) StartSendingMessage(msg);
                        return TrySend(channel, msg);
                    }
                    case ChannelKind.Unreliable:
                    {
                        var msg = new UdpMessage();
                        msg.SetPeerId(PeerId);
                        msg.Serialize(data, policy, encryptor);
                        return TrySend(channel, msg);
                    }
                    default:
                        throw new Exception($"Unknown channel: {channel}");
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
                return false;
            }
        }
        
        private bool TrySend(ChannelKind channel, IMessage message)
        {
            switch (channel)
            {
                case ChannelKind.Reliable when !(message is TcpMessage):
                case ChannelKind.Unreliable when !(message is UdpMessage):
                    return false;
                default:
                    return _channelRouter.TrySend(channel, message);
            }
        }

        public void SendPing()
        {
            var now = GetCurrentTimeMs();
            var ping = new C2SEngineProtocolData.Ping
            {
                SendTimeMs = now,
                RawRttMs = LatencyStats.LastRttMs,
                AvgRttMs = LatencyStats.AvgRttMs,
                JitterMs = LatencyStats.JitterMs,
                PacketLossRate = LatencyStats.PacketLossRate
            };

            try
            {
                InternalSend(ping);
                LatencyStats.OnSent();
            }
            finally
            {
                LastSendPingTime = now;
            }
        }

        private bool TrySetState(ENetPeerState compareState, ENetPeerState nextState)
        {
            if (Interlocked.CompareExchange(ref _stateCode, (int)nextState, (int)compareState) != (int)compareState)
                return false;
            
            OnStateChanged(compareState, nextState);
            return true;
        }

        private void SetState(ENetPeerState newState)
        {
            var oldState = (ENetPeerState)Interlocked.Exchange(ref _stateCode, (int)newState);
            OnStateChanged(oldState, newState);
        }

        private void OnStateChanged(ENetPeerState oldState, ENetPeerState newState)
        {
            StateChanged?.Invoke(this, new StateChangedEventArgs(oldState, newState));
        }

        private void SetTimer(Action<object> callback, object state, int dueTimeMs, int intervalMs)
        {
            _tickTimer?.Dispose();
            _tickTimer = new TickTimer(callback, state, dueTimeMs, intervalMs);
        }

        private void CancelTimer()
        {
            _tickTimer?.Dispose();
            _tickTimer = null;
        }

        private void StartPingTimer()
        {
            // 자동 핑 on/off 여부
            if (!Config.EnableAutoPing) return;
            SetTimer(_ => SendPing(), null, 0, Config.AutoPingIntervalSec * 1000);
        }

        private void StartConnection()
        {
            SetState(ENetPeerState.Connecting);

            ConnectAttempts = 0;
            SetTimer(OnConnectTimerTick, null, 0, Config.ConnectAttemptIntervalSec * 1000);
        }

        private TcpNetworkSession CreateNetworkSession()
        {
            var session = _session;
            session?.Close();
            
            session = new TcpNetworkSession(Logger, Config.SendQueueSize, Config.SendBufferSize, Config.ReceiveBufferSize);
            session.Opened += OnSessionOpened;
            session.Closed += OnSessionClosed;
            session.Error += OnSessionError;
            session.DataReceived += OnSessionDataReceived;
            
            LatencyStats.Clear();
            return session;
        }

        private void OnConnectTimerTick(object state)
        {
            if (++ConnectAttempts > Config.MaxConnectAttempts)
            {
                Logger?.Warn("Max connect attempts exceeded: {0} > {1}", ConnectAttempts, Config.MaxConnectAttempts);
                Close();
                return;
            }

            try
            {
                _session = CreateNetworkSession();
                _session.Connect(RemoteEndPoint);
            }
            catch (Exception e)
            {
                OnError(e);
            }
        }

        private void StartReconnection()
        {
            SetState(ENetPeerState.Reconnecting);

            ReconnectAttempts = 0;
            SetTimer(OnReconnectTimerTick, null, 0, Config.ReconnectAttemptIntervalSec * 1000);
        }

        private void OnReconnectTimerTick(object state)
        {
            if (++ReconnectAttempts > Config.MaxReconnectAttempts)
            {
                Logger?.Warn("Max reconnect attempts exceeded: {0} > {1}", ReconnectAttempts, Config.MaxReconnectAttempts);
                Close();
                return;
            }
            
            try
            {
                Logger?.Debug("Reconnecting... {0}", ReconnectAttempts);
                _session = CreateNetworkSession();
                _session.Connect(RemoteEndPoint);
            }
            catch (Exception e)
            {
                OnError(e);
            }
        }

        private void EnqueueSendingMessage(IMessage message)
        {
            _sendingMessageQueue.Enqueue(message);
        }

        private void SendAuthHandshake()
        {
            try
            {
                InternalSend(new C2SEngineProtocolData.SessionAuthReq
                {
                    SessionId = _sessionId,
                    PeerId = PeerId,
                    ClientPublicKey = _diffieHellman.PublicKey,
                    KeySize = _diffieHellman.KeySize
                });
            }
            catch (Exception e)
            {
                OnError(e);
            }
        }

        private void SendCloseHandshake()
        {
            InternalSend(new C2SEngineProtocolData.Close());
        }

        private void SendMessageAck(long sequenceNumber)
        {
            InternalSend(new C2SEngineProtocolData.MessageAck { SequenceNumber = sequenceNumber });
        }

        internal void CloseWithoutHandshake()
        {
            _session?.Close();
            _udpSocket?.Close();
        }

        public void Close()
        {
            if (State == ENetPeerState.Closing || State == ENetPeerState.Closed)
                return;
            
            if (TrySetState(ENetPeerState.None, ENetPeerState.Closing))
            {
                // 초기상태에서 종료된 경우
                OnClosed();
                return;
            }

            if (TrySetState(ENetPeerState.Connecting, ENetPeerState.Closing) 
                || TrySetState(ENetPeerState.Reconnecting, ENetPeerState.Closing))
            {
                var session = _session;
                if (session != null && session.IsConnected)
                {
                    // 세션이 연결되어 있으면 종료
                    session.Close();
                    return;
                }

                OnClosed();
                return;
            }
            
            SetState(ENetPeerState.Closing);

            // 종료 요청
            SendCloseHandshake();
            
            SetTimer(_ =>
            {
                if (_stateCode == (int)ENetPeerState.Closed) 
                    return;
            
                // 종료 요청에 대한 응답을 받지 못했으면 즉시 종료함
                CloseWithoutHandshake();
            }, null, 5000, Timeout.Infinite);
        }

        private void DequeueReceivedMessage()
        {
            // 연결된 상태에서만 메시지를 처리함
            if (!IsConnected)
                return;

            var count = 0;
            while (_receivedMessageQueue.TryDequeue(out var message))
            {
                switch (message)
                {
                    case TcpMessage _:
                    {
                        // 순서대로 처리하기위해 대기 중인 메시지들을 확인함
                        foreach (var inOrder in ProcessReceivedMessage(message))
                        {
                            OnMessageReceived(inOrder);
                        }

                        break;
                    }
                    case UdpMessage _:
                    {
                        OnMessageReceived(message);
                        break;
                    }
                }

                if (++count >= 20)
                    break;
            }
        }

        private void DequeueSendingMessage()
        {
            if (!IsConnected)
                return;

            var count = 0;
            while (_sendingMessageQueue.TryDequeue(out var message))
            {
                TrySend(ChannelKind.Reliable, message);
                if (++count >= 20)
                    break;
            }
        }

        private void CheckReSendMessages()
        {
            // 연결 상태일때만 체크함
            if (!IsConnected)
                return;

            // 메시지 재전송 
            foreach (var msg in GetResendCandidates())
                EnqueueSendingMessage(msg); 
        }

        private void OnSessionDataReceived(object sender, DataEventArgs e)
        {
            var span = e.Data.AsSpan(e.Offset, e.Length);
            _receiveBuffer.Write(span);

            try
            {
                foreach (var message in Filter())
                    ProcessMessage(message);
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
            finally
            {
                _receiveBuffer.MaybeTrim(TcpHeader.HeaderSize);
            }
        }

        private void ProcessMessage(IMessage message)
        {
            if (_engineHandlers.TryGetValue(message.Id, out var handler))
            {
                handler.ExecuteMessage(this, message);
            }
            else
            {
                if (message.SequenceNumber > 0)
                    SendMessageAck(message.SequenceNumber);
                            
                EnqueueReceivedMessage(message);
            }
        }
        
        private IEnumerable<TcpMessage> Filter()
        {
            const int maxFramesPerTick = 128;
            var produced = 0;
            
            while (produced < maxFramesPerTick)
            {
                if (_receiveBuffer.ReadableBytes < TcpHeader.HeaderSize)
                    yield break;
                
                var headerSpan = _receiveBuffer.Peek(TcpHeader.HeaderSize);
                var result = TcpHeader.TryParse(headerSpan, out var header);

                switch (result)
                {
                    case TcpHeader.ParseResult.NeedMore:
                        yield break;
                    case TcpHeader.ParseResult.Invalid:
                        Close(); 
                        yield break;
                    case TcpHeader.ParseResult.Success:
                    {
                        long frameLen = header.Length + header.PayloadLength;
                        if (frameLen <= 0 || frameLen > MaxFrameBytes)
                        {
                            Logger.Warn("Frame too large/small. max={0}, got={1}, (id={2})", 
                                MaxFrameBytes, frameLen, header.Id);
                            Close();
                            yield break;
                        }

                        var len = (int)frameLen;
                        if (_receiveBuffer.ReadableBytes < len)
                            yield break;

                        var frameBytes = _receiveBuffer.ReadBytes(len);
                        yield return new TcpMessage(header, new ArraySegment<byte>(frameBytes));
                        produced++;
                        break;
                    }
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        private void OnSessionError(object sender, ErrorEventArgs e)
        {
            OnError(e);
            OnClosed();
        }

        private void OnSessionClosed(object sender, EventArgs e)
        {
            OnClosed();
        }

        private void OnSessionOpened(object sender, EventArgs e)
        {
            _channelRouter.Bind(new ReliableChannel(_session));
            SetState(ENetPeerState.Handshake);
            SendAuthHandshake();
        }
        
        /// <summary>
        /// 수신된 메시지를 큐에 넣습니다.
        /// </summary>
        /// <param name="message"></param>
        private void EnqueueReceivedMessage(IMessage message)
        {
            _receivedMessageQueue.Enqueue(message);
        }

        private void OnError(ErrorEventArgs e)
        {
            Error?.Invoke(this, e);
        }

        private void OnError(Exception ex)
        {
            Error?.Invoke(this, new ErrorEventArgs(ex));
        }

        private void OnError(EngineErrorCode errorCode)
        {
            OnError(new Exception($"The system error occurred: {errorCode}"));
        }

        private void OnClosed()
        {
            switch (State)
            {
                case ENetPeerState.Connecting:
                case ENetPeerState.Reconnecting:
                    // 최초 연결 또는 재 연결 중일때는 타이머에서 종료 처리
                    break;
                case ENetPeerState.Open:
                    // 오프라인 전환
                    OnOffline();
                    break;
                case ENetPeerState.None:
                case ENetPeerState.Handshake:
                case ENetPeerState.Closing:
                {
                    SetState(ENetPeerState.Closed);
                    CancelTimer();
                    ResetMessageProcessor();
                    _session = null;
                    _udpSocket = null;
                    Disconnected?.Invoke(this, EventArgs.Empty);
                    break;
                }
                case ENetPeerState.Closed:
                    // 이미 종료됨
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void ConnectUdpSocket(int port)
        {
            if (port <= 0)
                return;
            
            var socket = new UdpSocket(Config.UdpMtu);
            socket.Error += OnUdpSocketError;
            socket.DataReceived += OnUdpSocketDataReceived;
            socket.Closed += OnUdpSocketClosed;
            
            var ipAddress = ((IPEndPoint)RemoteEndPoint).Address;
            if (!socket.Connect(ipAddress.ToString(), port))
            {
                Logger.Error("Failed to connect to UDP socket. ip={0}, port={1}", ipAddress.ToString(), port);
                return;
            }

            _udpSocket = socket;
            SendUdpHandshake();
        }

        private void OnUdpSocketClosed(object sender, EventArgs e)
        {
            _channelRouter.Unbind(ChannelKind.Unreliable);
            UdpClosed?.Invoke(this, EventArgs.Empty);
        }

        private void OnUdpSocketError(object sender, ErrorEventArgs e)
        {
            var ex = e.GetException();
            OnError(ex);
        }

        private void OnUdpSocketDataReceived(object sender, DataEventArgs e)
        {
            if (_udpSocket == null)
                return;
            
            var headerSpan = e.Data.AsSpan(e.Offset, e.Length);
            if (!UdpHeader.TryParse(headerSpan, out var header))
                return;

            if (PeerId != header.PeerId)
                return;
            
            IMessage message = null;
            if (header.IsFragmentation)
            {
                var span = e.Data.AsSpan(e.Offset, e.Length);
                if (UdpFragment.TryParse(span, out var fragment) &&
                    _udpSocket.Assembler.TryAssemble(fragment, out var payload))
                {
                    message = new UdpMessage(header, payload);
                }
            }
            else
            {
                var payload = new ArraySegment<byte>(e.Data, e.Offset, e.Length);
                message = new UdpMessage(header, payload);
            }

            if (message == null)
                return;

            try
            {
                // 엔진 프로토콜은 즉시 처리함
                if (_engineHandlers.TryGetValue(message.Id, out var handler))
                    handler.ExecuteMessage(this, message);
                else
                    EnqueueReceivedMessage(message);
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
        }

        private void SendUdpHandshake()
        {
            InternalSend(new C2SEngineProtocolData.UdpHelloReq
            {
                SessionId = _sessionId,
                PeerId = PeerId,
                Mtu = Config.UdpMtu
            });
        }

        internal void OnAuthHandshaked(S2CEngineProtocolData.SessionAuthAck p)
        {
            if (p.ErrorCode != EngineErrorCode.Success)
            {
                OnError(p.ErrorCode);
                CloseWithoutHandshake();
                return;
            }
            
            if (p.MaxFrameBytes > 0) MaxFrameBytes = p.MaxFrameBytes;
            if (p.SendTimeoutMs > 0) SetSendTimeoutMs(p.SendTimeoutMs);
            if (p.MaxResendCount > 0) SetMaxResendCnt(p.MaxResendCount);

            if (p.UseEncrypt)
            {
                var sharedKey = _diffieHellman.DeriveSharedKey(p.ServerPublicKey);
                _encryptor = new AesCbcEncryptor(sharedKey);   
            }

            // 정책 적용
            var g = new PolicyGlobals(p.UseEncrypt, p.UseCompress, p.CompressionThreshold);
            var newView = new SnapshotPolicyView(g);
            Interlocked.Exchange(ref _policyView, newView);
            
            SetState(ENetPeerState.Open);

            PeerId = p.PeerId;
            _sessionId = p.SessionId;
            
            // 핑 타이머 시작
            StartPingTimer();
            
            ConnectUdpSocket(p.UdpOpenPort);
            Connected?.Invoke(this, EventArgs.Empty);
        }

        internal void OnUdpHelloAck(EngineErrorCode errorCode)
        {
            if (errorCode != EngineErrorCode.Success)
            {
                _udpSocket.Close();
                _udpSocket = null;
                UdpClosed?.Invoke(this, EventArgs.Empty);
                return;
            }
            
            _channelRouter.Bind(new UnreliableChannel(_udpSocket));
            UdpOpened?.Invoke(this, EventArgs.Empty);

            if (Config.EnableUdpKeepAlive)
                _udpKeepAliveTimer = new TickTimer(SendUdpKeepAlive, null, 0, Config.UdpKeepAliveIntervalSec * 1000);
        }
        
        private void SendUdpKeepAlive(object state)
        {
            var keepAlive = new C2SEngineProtocolData.UdpKeepAlive();
            InternalSend(keepAlive);
        }
        
        private void OnOffline()
        {
            _policyView = new SnapshotPolicyView(PolicyDefaults.Globals);
            ResetSendingMessageState();
            Offline?.Invoke(this, EventArgs.Empty);
            StartReconnection();
        }
        
        private void OnMessageReceived(IMessage message)
        {
            MessageReceived?.Invoke(this, new MessageReceivedEventArgs(message));
        }

        internal void OnPong(long sendTimeMs, long serverTimeMs)
        {
            var now = GetCurrentTimeMs();
            var rawRttMs = now - sendTimeMs;
            LatencyStats.OnReceived(rawRttMs);
            RecordRttSample(rawRttMs);

            var estimatedServerTime = serverTimeMs + rawRttMs / 2.0;
            _serverTimeOffsetMs = estimatedServerTime - now;
        }
        
        internal void OnMessageAck(long sequenceNumber)
        {
            OnAckReceived(sequenceNumber);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                var session = _session;
                if (null != session)
                {
                    session.Opened -= OnSessionOpened;
                    session.Closed -= OnSessionClosed;
                    session.Error -= OnSessionError;
                    session.DataReceived -= OnSessionDataReceived;
                    
                    if (session.IsConnected)
                        session.Close();
                    
                    _session = null;
                }

                if (_udpSocket != null)
                {
                    _udpSocket.DataReceived -= OnUdpSocketDataReceived;
                    _udpSocket.Error -= OnUdpSocketError;
                    _udpSocket.Close();
                    _udpSocket = null;
                }
                
                _channelRouter.Unbind(ChannelKind.Reliable);
                _channelRouter.Unbind(ChannelKind.Unreliable);
                _diffieHellman.Dispose();
                _encryptor?.Dispose();
                
                _sessionId = null;
                PeerId = PeerId.None;
                LatencyStats.Clear();
                CancelTimer();
                ResetMessageProcessor();
            }

            _disposed = true;
        }
    }
}
