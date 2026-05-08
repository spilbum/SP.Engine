using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
        private readonly ReliableMessageProcessor _messageProcessor = new ReliableMessageProcessor();
        private SessionReceiveBuffer _receiveBuffer;
        private readonly ConcurrentQueue<IMessage> _messageReceivedQueue = new ConcurrentQueue<IMessage>();
        private Lz4Compressor _compressor;
        private bool _disposed;
        private AesGcmEncryptor _encryptor;

        private IProtocolPolicySnapshot _policySnapshot;
        private TcpNetworkSession _tcpSession;
        private long _sessionId;
        private int _stateCode;
        private TickTimer _timer;
        private UdpNetworkSession _udpSession;
        
        private Timer _ackTimer;
        private uint _lastSentAck;
        private long _lastAckTimerResetTicks;
        private readonly object _ackLock = new object();

        private TickTimer _udpHealthCheckTimer;
        private TickTimer _udpHandshakeTimer;
        private int _udpHealthFailCount;
        private TickTimer _udpCleanupTimer;

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

        protected IEncryptor Encryptor => _encryptor;
        protected ICompressor Compressor => _compressor;
        public ILogger Logger { get; private set; }

        public bool IsUdpAvailable => _channelRouter.IsUdpAvailable;

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

        public void Test_Reconnect()
        {
            _tcpSession?.Close();
        }

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
            if (null != _tcpSession)
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
            _udpHandshakeTimer?.Tick();
            _udpHealthCheckTimer?.Tick();
            _udpCleanupTimer?.Tick();

            DequeueMessageReceived();
            DequeuePendingMessage();
            ProcessRetransmissions();
        }

        private void DequeueMessageReceived()
        {
            var processed = 0;
            while (_messageReceivedQueue.TryDequeue(out var message))
            {
                using (message)
                {
                    try
                    {
                        if (message is TcpMessage tcp && tcp.SequenceNumber > 0)
                        {
                            var messages = _messageProcessor.IngestReceivedMessage(tcp);
                            if (messages == null) continue;

                            foreach (var m in messages)
                            {
                                using (m) DispatchCommand(m);
                            }
                        }
                        else
                        {
                            DispatchCommand(message);
                        }
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex, "Message processing failed. Id={0}", message.Id);
                    }
                }

                if (++processed >= 100) break;
            }
        }

        private void DispatchCommand(IMessage message)
        {
            var internalCommand = GetInternalCommand(message.Id);
            if (internalCommand != null)
            {
                internalCommand.Execute(this, message);
                return;
            }

            var command = GetUserCommand(message.Id);
            if (command == null)
            {
                Logger.Warn("Unknown command: {0}", message.Id);
                return;
            }

            var protocolData = command.Deserialize(this, message);
            command.Execute(this, protocolData);
        }
        
        private void DequeuePendingMessage()
        {
            if (!IsConnected)
                return;

            var pending = _messageProcessor.DequeuePendingMessages();
            foreach (var message in pending)
            {
                using (message)
                {
                    _messageProcessor.PrepareReliableSend(message);
                    if (!TrySend(ChannelKind.Reliable, message)) continue;

                    lock (_ackLock)
                    {
                        _lastSentAck = message.AckNumber;
                        RestartAckTimer();
                    }
                }
            }
        }
        
        private void ProcessRetransmissions()
        {
            // 연결 상태일때만 체크함
            if (!IsConnected)
                return;

            // 재전송할 메시지 추출
            var (retries, failed) = _messageProcessor.ProcessRetransmissions();
            if (failed != null && failed.Count > 0)
            {
                // 재전송 횟수 초과로 인해 실패됨
                Logger.Warn("Connection terminated due to message delivery failure. first seq: {0}, count: {1}",
                    failed[0].SequenceNumber, failed.Count);

                foreach (var message in failed)
                    message.Dispose();
                _messageProcessor.RestartInFlightMessages();
                
                _tcpSession?.Close();
                return;
            }

            if (retries != null)
            {
                foreach (var message in retries.Where(message => TrySend(ChannelKind.Reliable, message)))
                {
                    lock (_ackLock)
                    {
                        _lastSentAck = message.AckNumber;
                        RestartAckTimer();
                    }
                }
            }
        }

        public bool Send(IProtocolData data)
        {
            var policy = _policySnapshot.Resolve(data.Id);
            var encryptor = policy.UseEncrypt ? Encryptor : null;
            var compressor = policy.UseCompress ? Compressor : null;
            var originalChannel = data.Channel;
            var channel = originalChannel == ChannelKind.Unreliable && !_channelRouter.IsUdpAvailable
                ? ChannelKind.Reliable
                : originalChannel;

            try
            {
                switch (channel)
                {
                    case ChannelKind.Reliable:
                    {
                        var tcp = new TcpMessage();
                        using (tcp)
                        {
                            tcp.Serialize(data, policy, encryptor, compressor);

                            if (originalChannel == ChannelKind.Unreliable)
                                return TrySend(channel, tcp);

                            if (IsConnected)
                            {
                                _messageProcessor.PrepareReliableSend(tcp);
                                if (!TrySend(channel, tcp)) return false;

                                lock (_ackLock)
                                {
                                    _lastSentAck = tcp.AckNumber;
                                    RestartAckTimer();
                                }
                            }
                            else
                            {
                                _messageProcessor.EnqueuePendingMessage(tcp);
                            }

                            return true;
                        }
                    }
                    case ChannelKind.Unreliable:
                    {
                        if (!IsConnected) return false;
                        
                        var udp = new UdpMessage();
                        using (udp)
                        {
                            udp.SetSessionId(_sessionId);
                            udp.Serialize(data, policy, encryptor, compressor);
                            return TrySend(channel, udp);   
                        }
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

        private bool InternalSend(IProtocolData data)
        {
            var policy = PolicyDefaults.InternalPolicy;
            var encryptor = policy.UseEncrypt ? _encryptor : null;
            var compressor = policy.UseCompress ? _compressor : null;
            var channel = data.Channel == ChannelKind.Unreliable && !_channelRouter.IsUdpAvailable
                ? ChannelKind.Reliable
                : data.Channel;
            
            switch (channel)
            {
                case ChannelKind.Reliable:
                {
                    var tcp = new TcpMessage();
                    using (tcp)
                    {
                        tcp.Serialize(data, policy, encryptor, compressor);
                        return TrySend(channel, tcp);
                    }
                }
                case ChannelKind.Unreliable:
                {
                    var udp = new UdpMessage();
                    using (udp)
                    {
                        udp.SetSessionId(_sessionId);
                        udp.Serialize(data, policy, encryptor, compressor);
                        return TrySend(channel, udp);  
                    }
                }
                default:
                    throw new Exception($"Unknown channel: {channel}");
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

        private void SetTimer(Action<object> callback, object state, TimeSpan dueTime, TimeSpan period)
        {
            _timer?.Dispose();
            _timer = new TickTimer(callback, state, dueTime, period);
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
            SetTimer(_ => SendPing(), null, TimeSpan.Zero, TimeSpan.FromSeconds(Config.AutoPingIntervalSec));
        }

        private void StartConnection()
        {
            SetState(NetPeerState.Connecting);

            ConnectTryCount = 0;
            SetTimer(OnConnectTimerTick, null, TimeSpan.Zero, TimeSpan.FromSeconds(Config.ConnectAttemptIntervalSec));
        }

        private TcpNetworkSession CreateNetworkSession()
        {
            var session = _tcpSession;
            session?.Close();
            
            _receiveBuffer?.Dispose();
            _receiveBuffer = new SessionReceiveBuffer(Config.ReceiveBufferSize);

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
                _tcpSession = CreateNetworkSession();
                _tcpSession.Connect(RemoteEndPoint);
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
            SetTimer(OnReconnectTimerTick, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(Config.ReconnectAttemptIntervalSec));
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
                _tcpSession = CreateNetworkSession();
                _tcpSession.Connect(RemoteEndPoint);
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
            var ackNum = _messageProcessor.LastReceivedSeq;

            if (ackNum <= _lastSentAck) return;
            
            lock (_ackLock)
            {
                if (ackNum <= _lastSentAck) return;
                
                //Logger.Debug("[OnAckTimerTick] Timer expired. Sending ACK: {0}", ackNum);
                SendMessageAck(ackNum);
            }
        }

        private void SendMessageAck(uint ackNumber)
        {
            if (!InternalSend(new C2SEngineProtocolData.MessageAck { AckNumber = ackNumber }))
                return;
            
            _lastSentAck = ackNumber;
        }

        internal void CloseWithoutHandshake()
        {
            _tcpSession?.Close();
            _udpSession?.Close();
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
                var session = _tcpSession;
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
            }, null, TimeSpan.FromSeconds(5), Timeout.InfiniteTimeSpan);
        }
        
        private void OnSessionDataReceived(object sender, DataEventArgs e)
        {
            if (!_receiveBuffer.Write(e.Data.AsSpan(e.Offset, e.Length))) return;

            try
            {
                const int maxBatch = 100;
                const long maxTicks = TimeSpan.TicksPerMillisecond * 5;
                var sw = Stopwatch.StartNew();
                var processed = 0;
                while (_receiveBuffer.TryExtract(MaxFrameBytes, out var header, out var bodyOwner, out var bodyLength))
                {
                    ProcessAckStatus(header.AckNumber);

                    var message = new TcpMessage(header, bodyOwner, bodyLength);
                    MessageReceived(message);

                    if (++processed >= maxBatch || ((processed & 15) == 0 && sw.ElapsedTicks > maxTicks)) break;
                }
            }
            catch (Exception ex)
            {
                OnError(ex);
                Close();
            }
        }

        private void ProcessAckStatus(uint remoteAckNumber)
        {
            HandleRemoteAck(remoteAckNumber);
                    
            var ackNumber = _messageProcessor.LastReceivedSeq;
            if (ackNumber - _lastSentAck < _messageProcessor.AckStepThreshold)
                return;
            
            lock (_ackLock)
            {
                if (ackNumber - _lastSentAck < _messageProcessor.AckStepThreshold) return;
                SendMessageAck(ackNumber);
                RestartAckTimer();
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
            _channelRouter.Bind(new ReliableChannel(_tcpSession));
            SetState(NetPeerState.Handshake);
            SendAuthHandshake();
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
                    _messageProcessor.Dispose();
                    CancelTimer();

                    StopUdpHealthCheckTimer();
                    StopUdpHandshakeTimer();
                    StopAckTimer();
                    
                    _receiveBuffer?.Dispose();
                    while (_messageReceivedQueue.TryDequeue(out var message)) message.Dispose();
                    _messageReceivedQueue.Clear();
                    
                    _udpSession?.Close();
                    _udpSession = null;

                    PeerId = 0;
                    _sessionId = 0;
                    _tcpSession = null;

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

        private void ConnectUdpSocket(int openPort, int assemblyTimeoutSec, int maxPendingMessageCount, int cleanupIntervalSec)
        {
            if (openPort <= 0) return;
            
            var session = new UdpNetworkSession(Config);
            session.Error += OnUdpSocketError;
            session.DataReceived += OnUdpSocketDataReceived;
            session.Closed += OnUdpSocketClosed;

            if (!(RemoteEndPoint is IPEndPoint ep)) return;
            
            if (!session.Connect(ep.Address.ToString(), openPort))
            {
                Logger.Error("Failed to connect to UDP socket. ip={0}, port={1}", ep.Address, openPort);
                return;
            }
            
            session.SetupAssembler(assemblyTimeoutSec, maxPendingMessageCount);
            StartUdpCleanupTimer(cleanupIntervalSec);

            _udpSession = session;
            _channelRouter.Bind(new UnreliableChannel(session));
            
            StartUdpHandshakeTimer();
            SendUdpHandshake();
        }

        private void OnUdpSocketClosed(object sender, EventArgs e)
        {
            StopUdpHealthCheckTimer();
            StopUdpCleanupTimer();
            _channelRouter.Unbind(ChannelKind.Unreliable);
        }

        private void OnUdpSocketError(object sender, ErrorEventArgs e)
        {
            var ex = e.GetException();
            OnError(ex);
        }

        private void OnUdpSocketDataReceived(object sender, DataEventArgs e)
        {
            if (_udpSession == null) return;

            var span = e.Data.AsSpan(e.Offset, e.Length);
            if (!UdpHeader.TryRead(span, out var header, out var headerConsumed)) return;
            
            try
            {
                var bodyData = span.Slice(headerConsumed, header.BodyLength);
                if (header.IsFragmented)
                {
                    if (_udpSession.Assembler.TryPush(header, bodyData, out var message))
                    {
                        MessageReceived(message);
                    }
                }
                else
                {
                    IMemoryOwner<byte> owner = null;
                    if (header.BodyLength > 0)
                    {
                        var pooled = new PooledBuffer(header.BodyLength);
                        bodyData.CopyTo(pooled.Memory.Span);
                        owner = pooled;
                    }
            
                    var message = new UdpMessage(header, owner, header.BodyLength);
                    MessageReceived(message);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }

        private void MessageReceived(IMessage message)
        {
            var command = GetInternalCommand(message.Id);
            if (command != null)
            {
                using (message) command.Execute(this, message);
            }
            else
            {
                _messageReceivedQueue.Enqueue(message);
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
            {
                throw new Exception("Failed to send UDP handshake");   
            }
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
                Close();
                return;
            }

            if (p.MaxFrameBytes > 0) MaxFrameBytes = p.MaxFrameBytes;
            if (p.SendTimeoutMs > 0) _messageProcessor.SetSendTimeoutMs(p.SendTimeoutMs);
            if (p.MaxRetries > 0) _messageProcessor.SetMaxRetransmissionCount(p.MaxRetries);
            if (p.MaxAckDelayMs > 0) _messageProcessor.SetMaxAckDelayMs(p.MaxAckDelayMs);
            if (p.AckStepThreshold > 0) _messageProcessor.SetAckStepThreshold(p.AckStepThreshold);
            if (p.MaxOutOfOrderCount > 0) _messageProcessor.SetMaxOutOfOrder(p.MaxOutOfOrderCount);

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

            if (p.UdpOpenPort > 0)
                ConnectUdpSocket(p.UdpOpenPort, p.UdpAssemblyTimeoutSec, p.UdpMaxPendingMessageCount, p.UdpCleanupIntervalSec);
            
            Connected?.Invoke(this, EventArgs.Empty);
        }

        internal void OnUdpHandshake(S2CEngineProtocolData.UdpHelloAck p)
        {
            StopUdpHandshakeTimer();
            
            if (_udpSession == null) return;
            if (p.Result != UdpHandshakeResult.Ok)
            {
                _udpSession.Close();
                _udpSession = null;
                Logger.Error("UDP handshake failed: {0}", p.Result);
                return;
            }

            _channelRouter.SetUdpAvailable(true);
            _udpSession.SetMaxFrameSize(p.Mtu);
            StartUdpHealthCheckTimer();
        }

        private void StartUdpHealthCheckTimer()
        {
            StopUdpHealthCheckTimer();
            _udpHealthCheckTimer = new TickTimer(
                OnUdpHealthCheckTick, null, 
                TimeSpan.Zero, TimeSpan.FromSeconds(Config.UdpHealthCheckIntervalSec));
        }

        private void StopUdpHealthCheckTimer()
        {
            if (_udpHealthCheckTimer == null) return;
            _udpHealthCheckTimer.Dispose();
            _udpHealthCheckTimer = null;
        }

        private void OnUdpHealthCheckTick(object state)
        {
            if (!IsConnected || _udpSession == null || !_channelRouter.IsUdpAvailable) return;

            _udpHealthFailCount++;
            if (_udpHealthFailCount > Config.MaxUdpHealthFail)
            {
                _udpHealthFailCount = 0;
                if (_channelRouter.IsUdpAvailable)
                {
                    Logger.Warn("Session {0} UDP HealthCheck failed. Switching to TCP.", _sessionId, _udpHealthFailCount);
                    _channelRouter.SetUdpAvailable(false);
                }
            }

            InternalSend(new C2SEngineProtocolData.UdpHealthCheckReq());
        }

        internal void OnUdpHealthCheckAck()
        {
            _udpHealthFailCount = 0;
            if (!_channelRouter.IsUdpAvailable)
            {
                _channelRouter.SetUdpAvailable(true);
                Logger.Warn("Session {0} UDP HealthCheck restored.", _sessionId);
            }
            
            InternalSend(new C2SEngineProtocolData.UdpHealthCheckConfirm());
        }
        
        private int _udpHandshakeCount;

        private void StartUdpHandshakeTimer()
        {
            _udpHandshakeTimer = new TickTimer(_ =>
            {
                var count = Interlocked.Increment(ref _udpHandshakeCount) + 1;
                if (count >= 3)
                {
                    StopUdpHandshakeTimer();
                    _channelRouter.SetUdpAvailable(false);
                    Logger.Error("UDP handshake failed (timed out: {0} sec).", Config.UdpHandshakeTimeSec * count);
                }
                
                SendUdpHandshake();
            }, null, TimeSpan.FromSeconds(Config.UdpHandshakeTimeSec), TimeSpan.FromSeconds(Config.UdpHandshakeTimeSec));
        }

        private void StopUdpHandshakeTimer()
        {
            _udpHandshakeTimer?.Dispose();
            _udpHandshakeTimer = null;
        }

        private void StartUdpCleanupTimer(int periodSec)
        {
            if (_udpSession?.Assembler == null) return;
            _udpCleanupTimer = new TickTimer(_ =>
            {
                var now = DateTime.UtcNow;
                _udpSession.Assembler.Cleanup(now);
            }, null, TimeSpan.Zero, TimeSpan.FromSeconds(periodSec));
        }

        private void StopUdpCleanupTimer()
        {
            _udpCleanupTimer?.Dispose();
            _udpCleanupTimer = null;
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
            StopUdpCleanupTimer();
            
            // UDP 연결 해제
            _udpSession?.Close();
            _udpSession = null;
            
            // 재연결 시작
            StartReconnection();
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
            _messageProcessor.AcknowledgeInFlight(ackNumber);
            //Logger.Debug("[Ack] Remote acknowledged up to: {0}", ackNumber);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                var session = _tcpSession;
                if (null != session)
                {
                    session.Opened -= OnSessionOpened;
                    session.Closed -= OnSessionClosed;
                    session.Error -= OnSessionError;
                    session.DataReceived -= OnSessionDataReceived;

                    if (session.IsConnected)
                        session.Close();

                    _tcpSession = null;
                }

                StopUdpHealthCheckTimer();
                StopUdpHandshakeTimer();
                StopUdpCleanupTimer();
                
                if (_udpSession != null)
                {
                    _udpSession.DataReceived -= OnUdpSocketDataReceived;
                    _udpSession.Error -= OnUdpSocketError;
                    _udpSession.Close();
                    _udpSession = null;
                }
                
                _diffieHellman.Dispose();
                _sessionId = 0;
                _messageProcessor.Dispose();
                
                PeerId = 0;
                StopAckTimer();
                CancelTimer();
            }

            _disposed = true;
        }
    }
}
