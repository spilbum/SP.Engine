using System;
using System.Net;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using SP.Common.Buffer;
using SP.Common.Logging;
using SP.Engine.Client.Configuration;
using SP.Engine.Client.ProtocolHandler;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Compression;
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
        public NetPeerState OldState { get; private set; }
        public NetPeerState NewState { get; private set; }

        public StateChangedEventArgs(NetPeerState oldState, NetPeerState newState)
        {
            OldState = oldState;
            NewState = newState;
        }
    }

    public enum NetPeerState
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
        private UdpSocket _udp;
        private TickTimer _timer;
        private AesCbcEncryptor _encryptor;
        private readonly Lz4Compressor _compressor = new Lz4Compressor();
        private readonly BinaryBuffer _recvBuffer = new BinaryBuffer(1024);
        private readonly ConcurrentQueue<IMessage> _receivedMessageQueue = new ConcurrentQueue<IMessage>();
        private readonly Dictionary<ushort, IHandler<NetPeer, IMessage>> _engineHandlers =
            new Dictionary<ushort, IHandler<NetPeer, IMessage>>();
        private double _serverTimeOffsetMs;
        private TickTimer _keepAliveTimer;
        private readonly DiffieHellman _diffieHellman = new DiffieHellman(DhKeySize.Bit2048);
        private readonly ChannelRouter _channelRouter = new ChannelRouter();
        private IPolicyView _networkPolicy = new NetworkPolicyView(in PolicyDefaults.Globals);
        private int _recvBudgetMs = 1;
        private int _tcpParseBudgetMs = 1;
        private int _maxTcpBytesPerTick = 128 * 1024;
        private int _maxTcpFramesPerTickCap = 512;
        
        public int ConnectTryCount { get; private set; }
        public int ReconnectTryCount { get; private set; }
        public int MaxFrameBytes { get; private set; }
        public long LastSendPingTime { get; private set; }
        public EndPoint RemoteEndPoint { get; private set; }
        public uint PeerId { get; private set; }
        public ILogger Logger { get; }
        public EngineConfig Config { get; }
        public LatencyStats LatencyStats { get; }
        public NetPeerState State => (NetPeerState)_stateCode;
        public bool IsConnected => State == NetPeerState.Open;
        public IEncryptor Encryptor => _encryptor;
        public ICompressor Compressor => _compressor;

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
            MaxFrameBytes = 64 * 1024;
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
        
        public void SetRecvBudgetMs(int ms) => _recvBudgetMs = ms;
        public void SetTcpBudgetMs(int ms) => _tcpParseBudgetMs = ms;
        public void SetMaxTcpBytesPerTick(int bytes) => _maxTcpBytesPerTick = bytes;
        public void SetMaxTcpFramesPerTickCap(int cap) => _maxTcpFramesPerTickCap = cap;

        public TrafficInfo GetTcpTrafficInfo()
            => _session?.GetTrafficInfo();

        public TrafficInfo GetUdpTrafficInfo() 
            => _udp?.GetTrafficInfo();

        protected override void OnDebug(string format, params object[] args)
        {
            Logger?.Debug(format, args);
        }

        protected override void OnRetryLimitExceeded(IMessage message, int count, int maxCount)
        {
            Logger?.Error("Message {0} exceeded max resend count ({1}/{2}).", message.Id, count, maxCount);
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
            _timer?.Tick();
            _keepAliveTimer?.Tick();
            _udp?.Tick();
            
            // 수신된 메시지 처리
            DequeueReceivedMessage();

            // 대기 중인 메시지 전송
            DequeuePendingMessage();

            // 재 전송 메시지 체크
            CheckRetryMessage();
        }

        private void DequeuePendingMessage()
        {
            if (!IsConnected)
                return;

            foreach (var msg in DequeuePendingMessages())
                TrySend(ChannelKind.Reliable, msg);
        }
        
        public bool Send(IProtocol data)
        {
            var sequenceNumber = data.Channel == ChannelKind.Reliable ? GetNextReliableSeq() : 0;
            var policy = _networkPolicy.Resolve(data.GetType());
            var encryptor = policy.UseEncrypt ? Encryptor : null;
            var compressor = policy.UseCompress ? Compressor : null;
            return TrySend(sequenceNumber, data, policy, encryptor, compressor);
        }

        private bool InternalSend(IProtocol data)
        { 
            var policy = _networkPolicy.Resolve(data.GetType());
            var encryptor = policy.UseEncrypt ? Encryptor : null;
            var compressor = policy.UseCompress ? Compressor : null;
            return TrySend(0, data, policy, encryptor, compressor);
        }
        
        private bool TrySend(long sequenceNumber, IProtocol data, IPolicy policy, IEncryptor encryptor, ICompressor compressor)
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
                        msg.Serialize(data, policy, encryptor, compressor);
                    
                        if (sequenceNumber > 0 && !IsConnected)
                        {
                            EnqueuePendingMessage(msg);
                            return true;
                        }
            
                        if (sequenceNumber > 0) RegisterMessageState(msg);
                        return TrySend(channel, msg);
                    }
                    case ChannelKind.Unreliable:
                    {
                        var msg = new UdpMessage();
                        msg.SetPeerId(PeerId);
                        msg.Serialize(data, policy, encryptor, compressor);
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
                if (InternalSend(ping))
                    LatencyStats.OnSent();
            }
            finally
            {
                LastSendPingTime = now;
            }
        }

        private bool TrySetState(NetPeerState compareState, NetPeerState nextState)
        {
            if (Interlocked.CompareExchange(ref _stateCode, (int)nextState, (int)compareState) != (int)compareState)
                return false;
            
            OnStateChanged(compareState, nextState);
            return true;
        }

        private void SetState(NetPeerState newState)
        {
            var oldState = (NetPeerState)Interlocked.Exchange(ref _stateCode, (int)newState);
            OnStateChanged(oldState, newState);
        }

        private void OnStateChanged(NetPeerState oldState, NetPeerState newState)
        {
            StateChanged?.Invoke(this, new StateChangedEventArgs(oldState, newState));
        }

        private void SetTimer(Action<object> callback, object state, int dueTimeMs, int intervalMs)
        {
            _timer?.Dispose();
            _timer = new TickTimer(callback, state, dueTimeMs, intervalMs);
        }

        private void CancelTimer()
        {
            _timer?.Dispose();
            _timer = null;
        }

        private void StartPingTimer()
        {
            // 자동 핑 on/off 여부
            if (!Config.EnableAutoPing) return;
            SetTimer(_ => SendPing(), null, 0, Config.AutoPingIntervalSec * 1000);
        }

        private void StartConnection()
        {
            SetState(NetPeerState.Connecting);

            ConnectTryCount = 0;
            SetTimer(OnConnectTimerTick, null, 0, Config.ConnectAttemptIntervalSec * 1000);
        }

        private TcpNetworkSession CreateNetworkSession()
        {
            var session = _session;
            session?.Close();
            
            session = new TcpNetworkSession(Config);
            session.Opened += OnSessionOpened;
            session.Closed += OnSessionClosed;
            session.Error += OnSessionError;
            session.DataReceived += OnSessionDataReceived;
            
            LatencyStats.Clear();
            return session;
        }

        private void OnConnectTimerTick(object state)
        {
            if (++ConnectTryCount > Config.MaxConnectAttempts)
            {
                Logger?.Warn("Max connect attempts exceeded: {0} > {1}", ConnectTryCount, Config.MaxConnectAttempts);
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
            SetState(NetPeerState.Reconnecting);

            ReconnectTryCount = 0;
            SetTimer(OnReconnectTimerTick, null, 0, Config.ReconnectAttemptIntervalSec * 1000);
        }

        private void OnReconnectTimerTick(object state)
        {
            if (++ReconnectTryCount > Config.MaxReconnectAttempts)
            {
                Logger?.Warn("Max reconnect attempts exceeded: {0} > {1}", ReconnectTryCount, Config.MaxReconnectAttempts);
                Close();
                return;
            }
            
            try
            {
                Logger?.Debug("Reconnecting... {0}", ReconnectTryCount);
                _session = CreateNetworkSession();
                _session.Connect(RemoteEndPoint);
            }
            catch (Exception e)
            {
                OnError(e);
            }
        }

        private void SendAuthHandshake()
        {
            try
            {
                if (!InternalSend(new C2SEngineProtocolData.SessionAuthReq
                    {
                        SessionId = _sessionId,
                        PeerId = PeerId,
                        ClientPublicKey = _diffieHellman.PublicKey,
                        KeySize = _diffieHellman.KeySize
                    }))
                {
                    throw new Exception("Failed to send auth handshake");
                }
            }
            catch (Exception e)
            {
                OnError(e);
            }
        }

        private void SendCloseHandshake()
        {
            if (!InternalSend(new C2SEngineProtocolData.Close()))
                throw new Exception("Failed to send close handshake");
        }

        private void SendMessageAck(long sequenceNumber)
        {
            if (!InternalSend(new C2SEngineProtocolData.MessageAck { SequenceNumber = sequenceNumber }))
                throw new Exception("Failed to send message ack");
        }

        internal void CloseWithoutHandshake()
        {
            _session?.Close();
            _udp?.Close();
        }

        public void Close()
        {
            if (State == NetPeerState.Closing || State == NetPeerState.Closed)
                return;
            
            if (TrySetState(NetPeerState.None, NetPeerState.Closing))
            {
                // 초기상태에서 종료된 경우
                OnClosed();
                return;
            }

            if (TrySetState(NetPeerState.Connecting, NetPeerState.Closing) 
                || TrySetState(NetPeerState.Reconnecting, NetPeerState.Closing))
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
            
            SetState(NetPeerState.Closing);

            // 종료 요청
            SendCloseHandshake();
            
            SetTimer(_ =>
            {
                try
                {
                    if (_stateCode == (int)NetPeerState.Closed) return;
                    CloseWithoutHandshake();
                }
                catch (Exception e)
                {
                    OnError(e);
                }
            }, null, 5000, Timeout.Infinite);
        }

        private void DequeueReceivedMessage()
        {
            if (!IsConnected) return;

            var sw = Stopwatch.StartNew();
            var budgetTicks = _recvBudgetMs <= 0
                ? long.MaxValue
                : TimeSpan.FromMilliseconds(_recvBudgetMs).Ticks;

            while (sw.ElapsedTicks < budgetTicks && _receivedMessageQueue.TryDequeue(out var message))
            {
                foreach (var msg in ProcessMessageInOrder(message))
                    OnMessageReceived(msg);
            }
        }
        
        private void CheckRetryMessage()
        {
            // 연결 상태일때만 체크함
            if (!IsConnected)
                return;

            // 메시지 재전송 
            foreach (var msg in FindExpiredForRetry())
                EnqueuePendingMessage(msg);
        }

        private void OnSessionDataReceived(object sender, DataEventArgs e)
        {
            var span = e.Data.AsSpan(e.Offset, e.Length);
            _recvBuffer.Write(span);

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
                _recvBuffer.MaybeTrim(TcpHeader.ByteSize);
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
                if (message is TcpMessage tcp)
                    SendMessageAck(tcp.SequenceNumber);
                            
                EnqueueReceivedMessage(message);
            }
        }
        
        private IEnumerable<TcpMessage> Filter()
        {
            var budgetTicks = _tcpParseBudgetMs <= 0
                ? long.MaxValue
                : TimeSpan.FromMilliseconds(_tcpParseBudgetMs).Ticks;
            
            var bytesBudget = _maxTcpBytesPerTick <= 0 ? long.MaxValue : _maxTcpBytesPerTick;
            var framesCap = _maxTcpFramesPerTickCap;
            
            var tickSw = Stopwatch.StartNew();
            var frames = 0;
            long bytesParsed = 0;
            
            while (frames < framesCap && tickSw.ElapsedTicks < budgetTicks)
            {
                if (_recvBuffer.ReadableBytes < TcpHeader.ByteSize)
                    yield break;
                
                var headerSpan = _recvBuffer.Peek(TcpHeader.ByteSize);
                if (!TcpHeader.TryParse(headerSpan, out var header, out var consumed))
                    yield break;

                long frameLen = consumed + header.PayloadLength;
                if (frameLen <= 0 || frameLen > MaxFrameBytes)
                {
                    Logger.Warn("Frame too large/small. max={0}, got={1}, (id={2})", 
                        MaxFrameBytes, frameLen, header.Id);
                    Close();
                    yield break;
                }

                var len = (int)frameLen;
                if (_recvBuffer.ReadableBytes < len)
                    yield break;

                if (bytesParsed + len > bytesBudget)
                    yield break;
                        
                _recvBuffer.Advance(consumed);
                var frameBytes = _recvBuffer.ReadBytes(len);
                bytesParsed += len;
                frames++;
                        
                yield return new TcpMessage(header, new ArraySegment<byte>(frameBytes));
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
            SetState(NetPeerState.Handshake);
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

        private void OnClosed()
        {
            switch (State)
            {
                case NetPeerState.Open:
                    OnOffline();
                    break;
                case NetPeerState.None:
                case NetPeerState.Handshake:
                case NetPeerState.Closing:
                {
                    SetState(NetPeerState.Closed);
                    CancelTimer();
                    ResetProcessorState();
                    
                    StopUdpKeepAliveTimer();
                    _udp?.Close();
                    _udp = null;
                    
                    _session = null;
                    _channelRouter.Unbind(ChannelKind.Reliable);
                    _channelRouter.Unbind(ChannelKind.Unreliable);
                    Disconnected?.Invoke(this, EventArgs.Empty);
                    break;
                }
                case NetPeerState.Connecting:
                case NetPeerState.Reconnecting:
                    // 최초 연결 또는 재 연결 중일때는 타이머에서 종료 처리
                    break;
                case NetPeerState.Closed:
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void ConnectUdpSocket(int port)
        {
            if (port <= 0)
                return;
            
            var socket = new UdpSocket(Config);
            socket.Error += OnUdpSocketError;
            socket.DataReceived += OnUdpSocketDataReceived;
            socket.Closed += OnUdpSocketClosed;
            
            var ipAddress = ((IPEndPoint)RemoteEndPoint).Address;
            if (!socket.Connect(ipAddress.ToString(), port))
            {
                Logger.Error("Failed to connect to UDP socket. ip={0}, port={1}", ipAddress.ToString(), port);
                return;
            }

            _udp = socket;
            _channelRouter.Bind(new UnreliableChannel(socket));
            SendUdpHandshake();
        }

        private void OnUdpSocketClosed(object sender, EventArgs e)
        {
            StopUdpKeepAliveTimer();
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
            if (_udp == null)
                return;
            
            // var headerSpan = e.Data.AsSpan(e.Offset, e.Length);
            // if (!UdpHeader.TryParse(headerSpan, out var header))
            //     return;
            //
            // if (PeerId != header.PeerId)
            //     return;
            //
            // IMessage message = null;
            // if (header.IsFragmentation)
            // {
            //     var span = e.Data.AsSpan(e.Offset, e.Length);
            //     if (UdpFragment.TryParse(span, out var fragment) &&
            //         _udp.TryAssemble(fragment, out var payload))
            //     {
            //         message = new UdpMessage(header, payload);
            //     }
            // }
            // else
            // {
            //     var payload = new ArraySegment<byte>(e.Data, e.Offset, e.Length);
            //     message = new UdpMessage(header, payload);
            // }
            //
            // if (message == null)
            //     return;
            //
            // try
            // {
            //     // 엔진 프로토콜은 즉시 처리함
            //     if (_engineHandlers.TryGetValue(message.Id, out var handler))
            //         handler.ExecuteMessage(this, message);
            //     else
            //         EnqueueReceivedMessage(message);
            // }
            // catch (Exception ex)
            // {
            //     OnError(ex);
            // }
        }

        private void SendUdpHandshake()
        {
            if (!InternalSend(new C2SEngineProtocolData.UdpHelloReq
                {
                    SessionId = _sessionId,
                    PeerId = PeerId,
                    Mtu = Config.UdpMtu
                }))
            {
                throw new Exception($"Failed to send UDP handshake");
            }
        }

        internal void OnAuthHandshaked(S2CEngineProtocolData.SessionAuthAck p)
        {
            if (p.Result != SessionHandshakeResult.Ok)
            {
                OnError(new Exception($"Session auth failed: {p.Result}"));
                CloseWithoutHandshake();
                return;
            }
            
            if (p.MaxFrameBytes > 0) MaxFrameBytes = p.MaxFrameBytes;
            if (p.SendTimeoutMs > 0) SetSendTimeoutMs(p.SendTimeoutMs);
            if (p.MaxRetryCount > 0) SetMaxRetryCount(p.MaxRetryCount);

            if (p.UseEncrypt)
            {
                var sharedKey = _diffieHellman.DeriveSharedKey(p.ServerPublicKey);
                _encryptor = new AesCbcEncryptor(sharedKey);   
            }

            // 정책 적용
            var g = new PolicyGlobals(p.UseEncrypt, p.UseCompress, p.CompressionThreshold);
            var newView = new NetworkPolicyView(g);
            Interlocked.Exchange(ref _networkPolicy, newView);
            
            SetState(NetPeerState.Open);

            PeerId = p.PeerId;
            _sessionId = p.SessionId;
            
            // 핑 타이머 시작
            StartPingTimer();
            
            ConnectUdpSocket(p.UdpOpenPort);
            Connected?.Invoke(this, EventArgs.Empty);
        }

        internal void OnUdpHandshaked(S2CEngineProtocolData.UdpHelloAck p)
        {
            if (p.Result != UdpHandshakeResult.Ok)
            {
                _udp.Close();
                _udp = null;
                _channelRouter.Unbind(ChannelKind.Unreliable);
                UdpClosed?.Invoke(this, EventArgs.Empty);
                Logger?.Error("UDP handshake failed: {0}", p.Result);
                return;
            }
            
            _udp.SetMtu(p.Mtu);
            UdpOpened?.Invoke(this, EventArgs.Empty);

            if (Config.EnableUdpKeepAlive)
                StartUdpKeepAliveTimer();
        }

        private void StartUdpKeepAliveTimer()
        {
            StopUdpKeepAliveTimer();
            var internalMs = Config.UdpKeepAliveIntervalSec * 1000;
            _keepAliveTimer = new TickTimer(SendUdpKeepAlive, null, 0, internalMs);
        }

        private void StopUdpKeepAliveTimer()
        {
            if (_keepAliveTimer == null) return;
            _keepAliveTimer.Dispose();
            _keepAliveTimer = null;
        }
        
        private void SendUdpKeepAlive(object state)
        {
            var keepAlive = new C2SEngineProtocolData.UdpKeepAlive();
            InternalSend(keepAlive);
        }
        
        private void OnOffline()
        {
            _networkPolicy = new NetworkPolicyView(PolicyDefaults.Globals);
            ResetAllMessageStates();
            Offline?.Invoke(this, EventArgs.Empty);

            StopUdpKeepAliveTimer();
            _udp?.Close();
            _udp = null;
            
            _channelRouter.Unbind(ChannelKind.Unreliable);
            StartReconnection();
        }
        
        private void OnMessageReceived(IMessage message)
        {
            MessageReceived?.Invoke(this, new MessageReceivedEventArgs(message));
        }

        internal void OnPong(long sendTimeMs, long serverTimeMs)
        {
            var now = GetCurrentTimeMs();
            var rttMs = now - sendTimeMs;
            LatencyStats.OnReceived(rttMs);
            AddRtoSample(rttMs);

            var estimatedServerTime = serverTimeMs + rttMs / 2.0;
            _serverTimeOffsetMs = estimatedServerTime - now;
        }
        
        internal void OnMessageAck(long sequenceNumber)
        {
            RemoveMessageState(sequenceNumber);
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

                StopUdpKeepAliveTimer();
                if (_udp != null)
                {
                    _udp.DataReceived -= OnUdpSocketDataReceived;
                    _udp.Error -= OnUdpSocketError;
                    _udp.Close();
                    _udp = null;
                }
                
                _channelRouter.Unbind(ChannelKind.Reliable);
                _channelRouter.Unbind(ChannelKind.Unreliable);
                _diffieHellman.Dispose();
                _encryptor?.Dispose();
                
                _sessionId = null;
                PeerId = 0;
                LatencyStats.Clear();
                CancelTimer();
                ResetProcessorState();
            }

            _disposed = true;
        }
    }
}
