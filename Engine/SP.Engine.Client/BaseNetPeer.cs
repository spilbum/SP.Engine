using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using SP.Core;
using SP.Core.Logging;
using SP.Engine.Client.Command;
using SP.Engine.Client.Configuration;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Client
{
    public class StateChangedEventArgs : EventArgs
    {
        public StateChangedEventArgs(NetPeerState oldState, NetPeerState newState)
        {
            OldState = oldState;
            NewState = newState;
        }

        public NetPeerState OldState { get; private set; }
        public NetPeerState NewState { get; private set; }
    }

    public enum NetPeerState
    {
        None = 0,

        /// <summary>
        ///     최초 연결 시도
        /// </summary>
        Connecting = 1,

        /// <summary>
        ///     인증 처리중
        /// </summary>
        Handshake = 2,

        /// <summary>
        ///     연결 유지중
        /// </summary>
        Open = 3,

        /// <summary>
        ///     재연결 시도
        /// </summary>
        Reconnecting = 4,

        /// <summary>
        ///     종료중
        /// </summary>
        Closing = 5,

        /// <summary>
        ///     종료됨
        /// </summary>
        Closed = 6
    }

    public abstract class BaseNetPeer : ICommandContext, IDisposable
    {
        private readonly MessageChannelRouter _channelRouter = new MessageChannelRouter();
        private readonly DiffieHellman _diffieHellman = new DiffieHellman(DhKeySize.Bit2048);
        private readonly Dictionary<ushort, ICommand> _internalCommands = new Dictionary<ushort, ICommand>();
        private readonly Dictionary<ushort, ICommand> _userCommands = new Dictionary<ushort, ICommand>();
        private readonly ConcurrentQueue<IMessage> _receivedMessageQueue = new ConcurrentQueue<IMessage>();
        private readonly ReliableMessageProcessor _messageProcessor = new ReliableMessageProcessor();
        private PooledReceiveBuffer _receiveBuffer;
        private Lz4Compressor _compressor;
        private bool _disposed;
        private AesGcmEncryptor _encryptor;

        private IProtocolPolicySnapshot _policySnapshot;
        private TcpNetworkSession _session;
        private long _sessionId;
        private int _stateCode;
        private TickTimer _timer;
        private UdpSocket _udpSocket;
        
        private Timer _ackTimer;
        private volatile uint _lastSentAck;
        private long _lastAckTimerResetTicks;
        private readonly object _ackLock = new object();

        private TickTimer _udpHealthCheckTimer;
        private TickTimer _udpHandshakeTimer;
        private int _udpHealthFailCount;

        public int ConnectTryCount { get; private set; }
        public int ReconnectTryCount { get; private set; }
        public int MaxFrameBytes { get; private set; }
        public long LastPingTimeMs { get; private set; }
        public EndPoint RemoteEndPoint { get; private set; }
        public uint PeerId { get; private set; }
        public EngineConfig Config { get; private set; }
        public LatencyStats LatencyStats { get; private set; }
        public NetPeerState State => (NetPeerState)_stateCode;
        public bool IsConnected => State == NetPeerState.Open;
        
        public int InFlightCount => _messageProcessor.InFlightCount;
        public int OutOfOrderCount => _messageProcessor.OutOfOrderCount;
        public int PendingCount => _messageProcessor.PendingCount;
        public double SRttMs => _messageProcessor.SRttMs;
        public double JitterMs => _messageProcessor.JitterMs;

        protected IEncryptor Encryptor => _encryptor;
        protected ICompressor Compressor => _compressor;
        public ILogger Logger { get; private set; }

        TProtocol ICommandContext.Deserialize<TProtocol>(IMessage message)
        {
            return message.Deserialize<TProtocol>(_encryptor, _compressor);
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler Offline;
        public event EventHandler<ErrorEventArgs> Error;
        public event EventHandler<StateChangedEventArgs> StateChanged;

        protected void Initialize(EngineConfig config, ILogger logger)
        {
            Config = config;
            Logger = logger;
            MaxFrameBytes = 64 * 1024;
            LatencyStats = new LatencyStats();

            // 내부 프로토콜 핸들러 등록
            RegisterInternalCommand<SessionAuth>(S2CEngineProtocolId.SessionAuthAck);
            RegisterInternalCommand<Close>(S2CEngineProtocolId.Close);
            RegisterInternalCommand<MessageAck>(S2CEngineProtocolId.MessageAck);
            RegisterInternalCommand<Pong>(S2CEngineProtocolId.Pong);
            RegisterInternalCommand<UdpHelloAck>(S2CEngineProtocolId.UdpHelloAck);
            RegisterInternalCommand<UdpHealthCheckAck>(S2CEngineProtocolId.UdpHealthCheckAck);

            // 유저 프로토콜 핸들러 검색 및 등록
            var assembly = GetType().Assembly;
            DiscoverUserCommands(assembly);
            
            // 프로토콜 정책 초기화
            ProtocolPolicyRegistry.Initialize();
            // 초기 정책 설정 (인증 후 교체)
            _policySnapshot = ProtocolPolicyRegistry.CreateSnapshot(PolicyDefaults.FallbackGlobals);
        }

        ~BaseNetPeer()
        {
            Dispose(false);
        }

        private void RegisterInternalCommand<T>(ushort protocolId) where T : ICommand, new()
            => _internalCommands[protocolId] = new T();

        private ICommand GetInternalCommand(ushort protocolId)
        {
            _internalCommands.TryGetValue(protocolId, out var command);
            return command;
        }
        
        private void DiscoverUserCommands(Assembly assembly)
        {
            var type = GetType();
            foreach (var t in assembly.GetTypes())
            {
                if (!t.IsClass || t.IsAbstract) continue;
                if (!typeof(ICommand).IsAssignableFrom(t)) continue;
                
                var attr = t.GetCustomAttribute<ProtocolCommandAttribute>();
                if (attr == null)
                {
                    Logger.Warn($"[{t.FullName}] requires {nameof(ProtocolCommandAttribute)}");
                    continue;
                }
   
                if (!(Activator.CreateInstance(t) is ICommand command)) continue;
                if (type != command.ContextType) continue;
                if (!_userCommands.TryAdd(attr.Id, command))
                {
                    Logger.Warn($"Duplicate command: {attr.Id}");
                }
            }
            
            Logger.Debug("[NetPeer] Discovered '{0}' commands: [{1}]", _userCommands.Count, string.Join(", ", _userCommands.Keys));
        }

        private ICommand GetUserCommand(ushort protocolId)
        {
            _userCommands.TryGetValue(protocolId, out var command);
            return command;
        }

        private static readonly long _baseUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        private static long UtcNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        private static uint NetworkTimeMs => (uint)(UtcNowMs - _baseUnixMs);

        private EwmaFilter _serverTimeOffsetFilter = new EwmaFilter(0.1);
        private long _baseServerTimeOffsetMs;
        
        public DateTime GetServerTime()
        {
            var estimateMs = UtcNowMs + _baseServerTimeOffsetMs;
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

        public virtual void Tick()
        {
            _timer?.Tick();
            _udpSocket?.Tick();
            _udpHandshakeTimer?.Tick();
            _udpHealthCheckTimer?.Tick();

            // 수신된 메시지 처리
            DequeueReceivedMessage();

            // 보류된 메시지 전송
            DequeuePendingMessage();

            // 재 전송 메시지 체크
            CheckRetryMessage();
        }

        private void DequeueReceivedMessage()
        {
            if (!IsConnected) return;
            
            while (_receivedMessageQueue.TryDequeue(out var message))
            {
                switch (message)
                {
                    case TcpMessage tcp:

                        if (tcp.SequenceNumber == 0)
                        {
                            // 즉시 처리
                            OnMessageReceived(tcp);
                            break;
                        }
                        
                        var processedList = _messageProcessor.ProcessMessageInOrder(tcp);
                        if (processedList != null)
                        {
                            foreach (var msg in processedList)
                            {
                                OnMessageReceived(msg);
                            }
                        }
                        
                        break;
                    
                    case UdpMessage udp:
                        OnMessageReceived(udp);
                        break;
                }
            }
        }
        
        private void DequeuePendingMessage()
        {
            if (!IsConnected)
                return;

            foreach (var message in _messageProcessor.DequeuePendingMessages())
            {
                // 재전송 트래커에 등록
                _messageProcessor.RegisterMessageState(message);
                    
                // 전송
                if (!TrySend(ChannelKind.Reliable, message)) 
                    continue;
                
                lock (_ackLock)
                {
                    _lastSentAck = message.AckNumber;
                    RestartAckTimer();
                }
            }
        }
        
        private void CheckRetryMessage()
        {
            // 연결 상태일때만 체크함
            if (!IsConnected)
                return;

            // 재전송할 메시지 추출
            var (retries, failed) = _messageProcessor.ExtractRetryMessages();
            
            if (failed.Count > 0)
            {
                // 재전송 횟수 초과로 인해 실패됨
                Logger.Warn("Connection terminated due to message delivery failure. first seq: {0}", failed[0].SequenceNumber);
                // 재연결을 위해 종료 처리
                _session?.Close();
                return;
            }
            
            foreach (var message in retries.Where(message => TrySend(ChannelKind.Reliable, message)))
            {
                lock (_ackLock)
                {
                    _lastSentAck = message.AckNumber;
                    RestartAckTimer();
                }
            }
        }

        public bool Send(IProtocolData data)
        {
            var sequenceNumber = data.Channel == ChannelKind.Reliable
                ? _messageProcessor.GetNextReliableSeq()
                : 0;
            
            var ackNumber = data.Channel == ChannelKind.Reliable
                ? _messageProcessor.LastSequenceNumber
                : 0;
            
            return TrySend(sequenceNumber, ackNumber, data, _policySnapshot.Resolve(data.Id));
        }
        
        private bool InternalSend(IProtocolData data)
            => TrySend(0, 0, data, PolicyDefaults.InternalPolicy);

        private bool TrySend(uint sequenceNumber, uint ackNumber, IProtocolData data, IPolicy policy)
        {
            var encryptor = policy.UseEncrypt ? Encryptor : null;
            var compressor = policy.UseCompress ? Compressor : null;
            var channel = data.Channel == ChannelKind.Unreliable && !_channelRouter.IsUdpAvailable
                ? ChannelKind.Reliable
                : data.Channel;
            
            try
            {
                switch (channel)
                {
                    case ChannelKind.Reliable:
                    {
                        var tcp = new TcpMessage();
                        tcp.SetSequenceNumber(sequenceNumber);
                        tcp.SetAckNumber(ackNumber);
                        tcp.Serialize(data, policy, encryptor, compressor);

                        if (tcp.SequenceNumber == 0)
                        {
                            // 즉시 전송 (Internal/Fallback)
                            return TrySend(channel, tcp);
                        }
                            
                        if (!IsConnected)
                        {
                            // 메시지 팬딩 처리
                            if (_messageProcessor.EnqueuePendingMessage(tcp)) return true;
                            Logger.Error("Pending queue is full! Message dropped. Seq={0}", tcp.SequenceNumber);
                            return false;
                        }

                        // 전송 메시지 등록
                        _messageProcessor.RegisterMessageState(tcp);
                        
                        if (!TrySend(channel, tcp)) 
                            return false;
                        
                        lock (_ackLock)
                        {
                            _lastSentAck = tcp.AckNumber;   
                            RestartAckTimer();
                        }

                        return true;
                    }
                    case ChannelKind.Unreliable:
                    {
                        var udp = new UdpMessage();
                        udp.SetSessionId(_sessionId);
                        udp.Serialize(data, policy, encryptor, compressor);
                        return TrySend(channel, udp);
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
            var ping = new C2SEngineProtocolData.Ping
            {
                SendTimeMs = NetworkTimeMs,
                RawRttMs = LatencyStats.LastRttMs,
                AvgRttMs = LatencyStats.AvgRttMs,
                JitterMs = LatencyStats.JitterMs,
                PacketLossRate = 0
            };

            try
            {
                if (InternalSend(ping))
                    LatencyStats.OnSent();
            }
            finally
            {
                LastPingTimeMs = UtcNowMs;
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
            
            _receiveBuffer?.Dispose();
            _receiveBuffer = new PooledReceiveBuffer(Config.ReceiveBufferSize);

            session = new TcpNetworkSession(Config);
            session.Opened += OnSessionOpened;
            session.Closed += OnSessionClosed;
            session.Error += OnSessionError;
            session.DataReceived += OnSessionDataReceived;
            return session;
        }

        private void OnConnectTimerTick(object state)
        {
            if (++ConnectTryCount > Config.MaxConnectAttempts)
            {
                Logger.Warn("Max connect attempts exceeded: {0} > {1}", ConnectTryCount, Config.MaxConnectAttempts);
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
                Logger.Warn("Max reconnect attempts exceeded: {0} > {1}", ReconnectTryCount,
                    Config.MaxReconnectAttempts);
                Close();
                return;
            }

            try
            {
                Logger.Debug("Reconnecting... {0}", ReconnectTryCount);
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
                throw new Exception("Failed to send Close");
        }

        private void OnAckTimerTick(object state)
        {
            if (!IsConnected || _disposed) return;
            var currAckNumber = _messageProcessor.LastSequenceNumber;

            if (currAckNumber <= _lastSentAck) return;
            
            lock (_ackLock)
            {
                if (currAckNumber <= _lastSentAck) return;
                
                //Logger.Debug("[OnAckTimerTick] Timer expired. Sending ACK: {0}", currAckNumber);
                SendMessageAck(currAckNumber);
            }
        }

        private void SendMessageAck(uint ackNumber)
        {
            if (!InternalSend(new C2SEngineProtocolData.MessageAck { AckNumber = ackNumber }))
                throw new Exception("Failed to send MessageAck");
            
            _lastSentAck = ackNumber;
        }

        internal void CloseWithoutHandshake()
        {
            _session?.Close();
            _udpSocket?.Close();
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
        
        private void OnSessionDataReceived(object sender, DataEventArgs e)
        {
            if (_receiveBuffer == null) return;
            _receiveBuffer.Write(new ReadOnlySpan<byte>(e.Data, e.Offset, e.Length));

            try
            {
                const int headerSize = TcpHeader.ByteSize;
                const int maxProcessPerTick = 150;
                var processedCount = 0;

                Span<byte> headerSpan = stackalloc byte[headerSize];
                
                while (processedCount++ < maxProcessPerTick)
                {
                    if (_receiveBuffer == null || _receiveBuffer.ReadableBytes < headerSize)
                        break;

                    _receiveBuffer.Peek(headerSpan);
                    
                    if (!TcpHeader.TryRead(headerSpan, out var header, out var consumed))
                        break;

                    var bodyLen = header.PayloadLength;
                    
                    if (bodyLen > MaxFrameBytes)
                    {
                        Logger.Warn("Invalid payload length. id={0}, max={1}, len={2}",
                            header.ProtocolId, MaxFrameBytes, bodyLen);
                        Close();
                        break;
                    }

                    var total = consumed + bodyLen;
                    if (_receiveBuffer.ReadableBytes < total)
                        break;

                    _receiveBuffer.Consume(consumed);
                    
                    // ACK 처리
                    ProcessAckStatus(header.AckNumber);

                    if (bodyLen > 0)
                    {
                        var payload = new byte[bodyLen];
                        _receiveBuffer.Peek(payload);
                        _receiveBuffer.Consume(bodyLen);

                        OnReceivedMessage(new TcpMessage(header, payload));
                    }
                    else
                    {
                        OnReceivedMessage(new TcpMessage(header, ReadOnlyMemory<byte>.Empty));
                    }
                }
            }
            catch (Exception ex)
            {
                OnError(ex);
                Close();
            }
        }

        private void ProcessAckStatus(uint receivedAckNumber)
        {
            // 피기배킹 처리
            HandleRemoteAck(receivedAckNumber);
                    
            var currAckNumber = _messageProcessor.LastSequenceNumber;
            if (currAckNumber - _lastSentAck < _messageProcessor.AckStepThreshold)
                return;
            
            lock (_ackLock)
            {
                if (currAckNumber - _lastSentAck < _messageProcessor.AckStepThreshold)
                    return;
                
                //Logger.Debug("[Ack] Threshold ({0}). Sending immediate ACK: {1}", currAckNumber - _lastSentAck, currAckNumber);
                
                SendMessageAck(currAckNumber);
                RestartAckTimer();
            }
        }

        private void OnReceivedMessage(IMessage message)
        {
            var command = GetInternalCommand(message.Id);
            if (command != null)
            {
                command.Execute(this, message);
            }
            else
            {               
                EnqueueReceivedMessage(message);
            }
        }

        private void OnSessionError(object sender, ErrorEventArgs e)
        {
            OnError(e);
            OnClosed();
        }

        private void OnSessionClosed(object sender, EventArgs e)
        {
            _channelRouter.Unbind(ChannelKind.Reliable);
            OnClosed();
        }

        private void OnSessionOpened(object sender, EventArgs e)
        {
            _channelRouter.Bind(new ReliableChannel(_session));
            SetState(NetPeerState.Handshake);
            SendAuthHandshake();
        }

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
                    _messageProcessor.Clear();
                    CancelTimer();

                    StopUdpHealthCheckTimer();
                    StopUdpHandshakeTimer();
                    StopAckTimer();
                    
                    _receiveBuffer?.Dispose();
                    _receiveBuffer = null;
                    
                    _udpSocket?.Close();
                    _udpSocket = null;

                    PeerId = 0;
                    _sessionId = 0;
                    _session = null;

                    SetState(NetPeerState.Closed);
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

            _udpSocket = socket;
            _channelRouter.Bind(new UnreliableChannel(socket));
            
            StartUdpHandshakeTimer();
            SendUdpHandshake();
        }

        private void OnUdpSocketClosed(object sender, EventArgs e)
        {
            StopUdpHealthCheckTimer();
            _channelRouter.Unbind(ChannelKind.Unreliable);
        }

        private void OnUdpSocketError(object sender, ErrorEventArgs e)
        {
            var ex = e.GetException();
            OnError(ex);
        }

        private void OnUdpSocketDataReceived(object sender, DataEventArgs e)
        {
            if (_udpSocket == null) return;

            var segment = new ArraySegment<byte>(e.Data, e.Offset, e.Length);
            if (segment.Array == null) return;
            
            var headerSpan = segment.AsSpan(0, UdpHeader.ByteSize);
            if (!UdpHeader.TryRead(headerSpan, out var header, out var consumed))
                return;

            if (_sessionId != header.SessionId)
                return;

            var bodyOffset = consumed;

            if (header.Fragmented == 0x01)
            {
                var bodySpan = segment.AsSpan(bodyOffset, header.PayloadLength);
                if (!FragmentHeader.TryParse(bodySpan, out var fragHeader, out consumed))
                    return;
                
                var fragSegment = new ArraySegment<byte>(segment.Array, bodyOffset + consumed, fragHeader.FragLength);
                    
                if (!_udpSocket.Assembler.TryAssemble(fragHeader, fragSegment, out var assembled))
                    return;

                var normalizedHeader = new UdpHeaderBuilder()
                    .From(header)
                    .WithPayloadLength(assembled.Count)
                    .Build();

                var message = new UdpMessage(normalizedHeader, assembled);
                OnReceivedMessage(message);
            }
            else
            {
                var payload = new byte[header.PayloadLength];
                segment.AsSpan(bodyOffset, header.PayloadLength).CopyTo(payload);
                
                var message = new UdpMessage(header, payload);
                OnReceivedMessage(message);
            }
        }

        private void SendUdpHandshake()
        {
            if (!InternalSend(new C2SEngineProtocolData.UdpHelloReq
                {
                    SessionId = _sessionId,
                    PeerId = PeerId,
                    Mtu = Config.UdpMtu
                }))
                throw new Exception("Failed to send UDP handshake");
        }

        internal void OnSessionAuth(S2CEngineProtocolData.SessionAuthAck p)
        {
            if (p.Result != SessionAuthResult.Ok)
            {
#if DEBUG
                OnError(new Exception($"Session auth failed: {p.Result}, reason={p.Reason}"));
#else
                OnError(new Exception($"Session auth failed: {p.Result}"));
#endif
                OnClosed();
                return;
            }

            if (p.MaxFrameBytes > 0) MaxFrameBytes = p.MaxFrameBytes;
            if (p.SendTimeoutMs > 0) _messageProcessor.SetSendTimeoutMs(p.SendTimeoutMs);
            if (p.MaxRetries > 0) _messageProcessor.SetMaxRetryCount(p.MaxRetries);
            if (p.MaxAckDelayMs > 0) _messageProcessor.SetMaxAckDelayMs(p.MaxAckDelayMs);
            if (p.AckStepThreshold > 0) _messageProcessor.SetAckStepThreshold(p.AckStepThreshold);

            if (p.UseEncrypt)
            {
                var sharedKey = _diffieHellman.DeriveSharedKey(p.ServerPublicKey);
                _encryptor = new AesGcmEncryptor(sharedKey);
            }

            if (p.UseCompress) _compressor = new Lz4Compressor(p.MaxFrameBytes);

            // 서버로 부터 받은 글로벌 정책으로 적용
            var g = new PolicyGlobals(p.UseEncrypt, p.UseCompress, p.CompressionThreshold);
            var policySnapshot = ProtocolPolicyRegistry.CreateSnapshot(g);
            Interlocked.Exchange(ref _policySnapshot, policySnapshot);

            SetState(NetPeerState.Open);

            PeerId = p.PeerId;
            _sessionId = p.SessionId;

            // 핑 타이머 시작
            StartPingTimer();
            
            // Ack 타이머 시작
            StartAckTimer();

            ConnectUdpSocket(p.UdpOpenPort);
            Connected?.Invoke(this, EventArgs.Empty);
        }

        internal void OnUdpHandshake(S2CEngineProtocolData.UdpHelloAck p)
        {
            StopUdpHandshakeTimer();
            
            if (_udpSocket == null) return;
            
            if (p.Result != UdpHandshakeResult.Ok)
            {
                _udpSocket.Close();
                _udpSocket = null;
                Logger.Error("UDP handshake failed: {0}", p.Result);
                return;
            }

            _channelRouter.SetUdpAvailable(true);
            _udpSocket.SetMaxFrameSize(p.Mtu);
            StartUdpHealthCheckTimer();
        }

        private void StartUdpHealthCheckTimer()
        {
            StopUdpHealthCheckTimer();
            _udpHealthCheckTimer = new TickTimer(OnUdpHealthCheckTick, null, 0, Config.UdpHealthCheckIntervalSec * 1000);
        }

        private void StopUdpHealthCheckTimer()
        {
            if (_udpHealthCheckTimer == null) return;
            _udpHealthCheckTimer.Dispose();
            _udpHealthCheckTimer = null;
        }

        private void OnUdpHealthCheckTick(object state)
        {
            if (!IsConnected || _udpSocket == null || !_channelRouter.IsUdpAvailable) return;

            if (++_udpHealthFailCount > Config.MaxUdpHealthFail)
            {
                Logger.Warn("UDP HealthCheck failed. Final count: {0}. Switching to TCP.", _udpHealthFailCount);
                _channelRouter.SetUdpAvailable(false);
                return;
            }

            InternalSend(new C2SEngineProtocolData.UdpHealthCheckReq());
        }

        internal void OnUdpHealthCheckAck()
        {
            _udpHealthFailCount = 0;
            _channelRouter.SetUdpAvailable(true);
            InternalSend(new C2SEngineProtocolData.UdpHealthCheckConfirm());
        }

        private void StartUdpHandshakeTimer()
        {
            StopUdpHandshakeTimer();
            _udpHandshakeTimer = new TickTimer(_ =>
            {
                _channelRouter.SetUdpAvailable(false);
                Logger.Error("UDP handshake failed (timed out).");
            }, null, Config.UdpHandshakeTimeSec * 1000, Timeout.Infinite);
        }

        private void StopUdpHandshakeTimer()
        {
            if (_udpHandshakeTimer == null) return;
            _udpHandshakeTimer.Dispose();
            _udpHandshakeTimer = null;
        }

        private void StartAckTimer()
        {
            StopAckTimer();
            _ackTimer = new Timer(OnAckTimerTick, null,_messageProcessor.MaxAckDelayMs, _messageProcessor.MaxAckDelayMs);
        }

        private void RestartAckTimer()
        {
            var now = DateTime.UtcNow.Ticks;
            var maxAckDelayMs = _messageProcessor.MaxAckDelayMs;
            if (now - _lastAckTimerResetTicks < TimeSpan.TicksPerMillisecond * (maxAckDelayMs / 4))
                return;
            
            _lastAckTimerResetTicks = now;
            _ackTimer?.Change(maxAckDelayMs, maxAckDelayMs);
        }

        private void StopAckTimer()
        {
            if (_ackTimer == null) return;
            _ackTimer.Dispose();
            _ackTimer = null;
        }

        private void OnOffline()
        {
            Offline?.Invoke(this, EventArgs.Empty);
            
            // UDP 타이머 해제
            StopUdpHealthCheckTimer();
            StopUdpHandshakeTimer();
            
            // UDP 연결 해제
            _udpSocket?.Close();
            _udpSocket = null;
            
            // 재연결 시작
            StartReconnection();
        }

        private void OnMessageReceived(IMessage message)
        {
            var command = GetUserCommand(message.Id);
            if (command == null)
            {
                Logger.Warn("Not found command: {0}", message.Id);
                return;
            }

            command.Execute(this, message);
        }

        internal void OnPong(uint clientSendTimeMs, uint serverNetworkTimeMs)
        {
            var nowMs = NetworkTimeMs;
            var rttMs = nowMs - clientSendTimeMs;

            var estimatedServerNetworkTime = serverNetworkTimeMs + rttMs / 2;
            var currentSampleOffset = (long)estimatedServerNetworkTime - nowMs;

            _serverTimeOffsetFilter.Update(currentSampleOffset);
            _baseServerTimeOffsetMs = (long)_serverTimeOffsetFilter.Value;
            
            LatencyStats.OnReceived(rttMs);
            _messageProcessor.AddRtoSample(rttMs);
        }

        internal void OnMessageAck(uint ackNumber)
        {
            HandleRemoteAck(ackNumber);
        }

        private void HandleRemoteAck(uint ackNumber)
        {
            if (ackNumber <= 0) return;
            _messageProcessor.RemoveMessageStates(ackNumber);
            //Logger.Debug("[Ack] Remote acknowledged up to: {0}", ackNumber);
        }

        protected virtual void Dispose(bool disposing)
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

                StopUdpHealthCheckTimer();
                StopUdpHandshakeTimer();
                
                if (_udpSocket != null)
                {
                    _udpSocket.DataReceived -= OnUdpSocketDataReceived;
                    _udpSocket.Error -= OnUdpSocketError;
                    _udpSocket.Close();
                    _udpSocket = null;
                }
                
                _diffieHellman.Dispose();
                _sessionId = 0;
                _messageProcessor.Clear();
                _receiveBuffer?.Dispose();
                _receiveBuffer = null;
                
                PeerId = 0;
                StopAckTimer();
                CancelTimer();
            }

            _disposed = true;
        }
    }
}
