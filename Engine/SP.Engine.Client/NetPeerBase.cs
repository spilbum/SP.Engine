using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using SP.Core;
using SP.Core.Buffers;
using SP.Core.Logging;
using SP.Engine.Client.Command;
using SP.Engine.Client.Configuration;
using SP.Engine.Protocol;
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

    public abstract class NetPeerBase : ICommandContext, IDisposable
    {
        private static readonly long _baseUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        private static long UtcNowMs => DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        internal static uint NetworkTimeMs => (uint)(UtcNowMs - _baseUnixMs);
        
        private readonly MessageChannelRouter _channelRouter = new MessageChannelRouter();
        private readonly DiffieHellman _diffieHellman = new DiffieHellman(DhKeySize.Bit2048);
        private readonly Dictionary<ushort, ICommand> _internalCommands = new Dictionary<ushort, ICommand>();
        private readonly Dictionary<ushort, ICommand> _userCommands = new Dictionary<ushort, ICommand>();
        private ByteReadWriteBuffer _readWriteBuffer;
        private readonly ConcurrentQueue<IMessage> _messageReceivedQueue = new ConcurrentQueue<IMessage>();
        private Lz4Compressor _compressor;
        private bool _disposed;
        private AesGcmEncryptor _encryptor;
        private ReliableMessageProcessor _messageProcessor;

        private readonly Dictionary<ushort, ProtocolOverrides> _protocolOverrides = new Dictionary<ushort, ProtocolOverrides>();
        private IPolicySnapshot _policySnapshot;
        private TcpNetworkSession _tcpNetworkSession;
        private long _sessionId;
        private volatile uint _peerId;
        private int _stateCode;
        private TickTimer _timer;
        private UdpNetworkSession _udpNetworkSession;
        
        private uint _lastSentAck;
        private DateTime _lastAckTime;

        private FragmentAssembler _fragmentAssembler;
        private TickTimer _udpHandshakeTimer;
        private TickTimer _fragmentAssemblerCleanupTimer;
        private int _udpHandshakeCount;
        
        private readonly EwmaFilter _serverTimeOffsetFilter = new EwmaFilter(0.1);
        private long _baseServerTimeOffsetMs;
        
        private readonly List<TcpMessage> _retriesCache = new List<TcpMessage>();
        private readonly List<TcpMessage> _orderCache = new List<TcpMessage>();

        public uint PeerId => _peerId;
        
        public int ConnectTryCount { get; private set; }
        public int ReconnectTryCount { get; private set; }
        public long LastPingTimeMs { get; private set; }
        public EndPoint RemoteEndPoint { get; private set; }
        public EngineConfig Config { get; private set; }
        public LatencyStats LatencyStats { get; private set; }
        public NetPeerState State => (NetPeerState)_stateCode;
        public bool IsConnected => State == NetPeerState.Open;
        public ILogger Logger { get; private set; }
        
        IEncryptor ICommandContext.Encryptor => _encryptor;
        ICompressor ICommandContext.Compressor => _compressor;
        
        public DateTime ServerTime
        {
            get
            {
                var estimateMs = UtcNowMs + _baseServerTimeOffsetMs;
                return DateTimeOffset.FromUnixTimeMilliseconds(estimateMs).UtcDateTime;    
            }
        }
        
        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler Offline;
        public event EventHandler<ErrorEventArgs> Error;
        public event EventHandler<StateChangedEventArgs> StateChanged;

        public void Connect(string ip, int port)
        {
            if (null != _tcpNetworkSession)
                throw new InvalidOperationException("Already opened");

            if (string.IsNullOrEmpty(ip) || 0 >= port)
                throw new ArgumentException("Invalid ip or port");

            RemoteEndPoint = ResolveEndPoint(ip, port);
            StartConnection();
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
                var session = _tcpNetworkSession;
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
        
        public virtual void Tick()
        {
            _timer?.Tick();
            _udpHandshakeTimer?.Tick();
            _fragmentAssemblerCleanupTimer?.Tick();

            DequeueMessageReceived();
            CheckAndFlushPeriodAck();
            FlushPendingMessage();
            ProcessRetransmissions();
        }
        
        public bool Send(IProtocolData data)
        {
            var policy = _policySnapshot.Resolve(data.Id);
            var encryptor = policy.UseEncrypt ? _encryptor : null;
            var compressor = policy.UseCompress ? _compressor : null;
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
                        var message = MessagePool<TcpMessage>.Rent();
                        message.Serialize(data, policy, encryptor, compressor);

                        using (message)
                        {
                            if (originalChannel == ChannelKind.Unreliable)
                            {
                                return TrySend(channel, message);
                            }

                            if (IsConnected)
                            {
                                if (!_messageProcessor.RegisterInFlight(message, out var inFlightMessage))
                                {
                                    Logger.Warn("NetPeer {0} RegisterInFlight failed.", _peerId);
                                    return false;
                                }

                                if (!TrySend(channel, inFlightMessage)) return false;
                            }
                            else
                            {
                                if (!_messageProcessor.EnqueuePendingMessage(message))
                                {
                                    return false;
                                }
                            }
                            
                            return true;
                        }
                    }
                    case ChannelKind.Unreliable:
                    {
                        if (!IsConnected) return false;

                        var message = MessagePool<UdpMessage>.Rent();
                        message.Serialize(data, policy, encryptor, compressor);
                        message.SetSessionId(_sessionId);
                        using (message) return TrySend(channel, message);   
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
            
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        public void Test_Reconnect()
        {
            _tcpNetworkSession?.Close();
        }

        internal bool InternalInitialize(Assembly[] assemblies, EngineConfig config, ILogger logger)
        {
            Config = config;
            Logger = logger;
            LatencyStats = new LatencyStats();
            
            // 내부 프로토콜 핸들러 등록
            RegisterInternalCommand<SessionAuth>(S2CEngineProtocolId.SessionAuthAck);
            RegisterInternalCommand<Close>(S2CEngineProtocolId.Close);
            RegisterInternalCommand<MessageAck>(S2CEngineProtocolId.MessageAck);
            RegisterInternalCommand<Pong>(S2CEngineProtocolId.Pong);
            RegisterInternalCommand<UdpHelloAck>(S2CEngineProtocolId.UdpHelloAck);
            RegisterInternalCommand<UdpHealthCheck>(S2CEngineProtocolId.UdpHealthCheck);
            RegisterInternalCommand<UdpStatusNotify>(S2CEngineProtocolId.UdpStatusNotify);

            // 유저 프로토콜 핸들러 검색 및 등록
            if (!DiscoverUserCommands(assemblies))
                return false;

            if (!SetupPolicy(assemblies))
                return false;

            var defaultPolicy = new PolicyGlobals(false, false, 0, 65536);
            _policySnapshot = CreatePolicySnapshot(defaultPolicy);
            return true;
        }

        ~NetPeerBase()
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
        
        private bool DiscoverUserCommands(Assembly[] assemblies)
        {
            var targetType = GetType();
            
            foreach (var assembly in assemblies)
            {
                var types = assembly.GetTypes()
                    .Where(t => typeof(ICommand).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract)
                    .ToList();
                
                foreach (var t in types)
                {
                    var attr = t.GetCustomAttribute<ProtocolCommandAttribute>();
                    if (attr == null)
                    {
                        Logger.Warn($"[{t.FullName}] requires {nameof(ProtocolCommandAttribute)}");
                        continue;
                    }
   
                    if (!(Activator.CreateInstance(t) is ICommand command)) continue;
                    if (targetType != command.ContextType) continue;
                    if (!_userCommands.TryAdd(attr.Id, command))
                    {
                        Logger.Warn($"Duplicate command: {attr.Id}");
                    }
                }
            }

            if (_userCommands.Count == 0)
            {
                Logger.Fatal("Command could not be found");
                return false;
            }
            
            Logger.Debug("[NetPeer] Discovered '{0}' commands: [{1}]", _userCommands.Count, string.Join(", ", _userCommands.Keys));
            return true;
        }

        private ICommand GetUserCommand(ushort protocolId)
        {
            _userCommands.TryGetValue(protocolId, out var command);
            return command;
        }

        private bool SetupPolicy(Assembly[] assemblies)
        {
            foreach (var assembly in assemblies)
            {
                var types = assembly.GetTypes()
                    .Where(t => typeof(IProtocolData).IsAssignableFrom(t) && t.IsClass && !t.IsAbstract);

                foreach (var type in types)
                {
                    var attr = type.GetCustomAttribute<ProtocolAttribute>();
                    if (attr == null) return false;
                    _protocolOverrides[attr.Id] = new ProtocolOverrides(attr.Encrypt, attr.Compress, attr.MaxPayloadLength);
                }
            }

            return _protocolOverrides.Count != 0;
        }

        private IPolicySnapshot CreatePolicySnapshot(PolicyGlobals globals)
            => new PolicySnapshot(globals, _protocolOverrides);

        private static EndPoint ResolveEndPoint(string ip, int port)
        {
            if (IPAddress.TryParse(ip, out var address))
                return new IPEndPoint(address, port);
            return new DnsEndPoint(ip, port);
        }

        private void DequeueMessageReceived()
        {
            while (_messageReceivedQueue.TryDequeue(out var message))
            {
                _orderCache.Clear();
                
                try
                {
                    if (message is TcpMessage tcp && tcp.SequenceNumber > 0)
                    {
                        var result = _messageProcessor.ReceiveIngestMessage(tcp, _orderCache);
                        switch (result)
                        {
                            case ReceiveIngestResult.Success:
                            {
                                foreach (var m in _orderCache)
                                {
                                    using (m) DispatchCommand(m);
                                }

                                break;
                            }
                            case ReceiveIngestResult.BufferOverflow:
                            {
                                Logger.Warn("NetPeer {0} Out-of-order buffer overflow.", _peerId);
                                Close();
                                return;
                            }
                            case ReceiveIngestResult.Buffered:
                            case ReceiveIngestResult.Duplicate:
                            default:
                                break;
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
                finally
                {
                    message.Dispose();
                }
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

            command.Execute(this, message);
        }
        
        private void FlushPendingMessage()
        {
            if (!IsConnected) return;

            var messages = _messageProcessor.FlushPendingMessages();
            if (messages.Count == 0) return;
            
            var processed = 0;
            foreach (var message in messages)
            {
                if (!IsConnected) break;
                if (!_messageProcessor.RegisterInFlight(message, out var inFlightMessage)) break;
                
                TrySend(ChannelKind.Reliable, inFlightMessage);
                message.Dispose();
                    
                processed++; 
            }
            
            if (processed >= messages.Count) return;
            
            for (var index = processed; index < messages.Count; index++)
            {
                var message = messages[index];
                using (message)
                {
                    if (!_messageProcessor.EnqueuePendingMessage(message))
                    {
                        Logger.Warn("Failed to re-enqueue pending message during flush.");
                    }
                }
            }
        }
        
        private void ProcessRetransmissions()
        {
            // 연결 상태일때만 체크함
            if (!IsConnected)
                return;

            _retriesCache.Clear();
            
            try
            {
                var failed = _messageProcessor.PrepareRetransmissions(_retriesCache);
                if (failed != null)
                {
                    Logger.Warn("Retransmission exhausted. PeerId: {0}, Failed Seq: {1}, ProtocolId: {2}",
                        PeerId, failed.SequenceNumber, failed.Id);
                
                    _tcpNetworkSession?.Close();
                    return;
                }

                foreach (var message in _retriesCache)
                {
                    using (message) TrySend(ChannelKind.Reliable, message);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "ProcessRetransmissions failed: {0}", ex.Message);
            }
        }

        internal bool InternalSend(IProtocolData data)
        {
            var channel = data.Channel == ChannelKind.Unreliable && !_channelRouter.IsUdpAvailable
                ? ChannelKind.Reliable
                : data.Channel;
            
            switch (channel)
            {
                case ChannelKind.Reliable:
                {
                    var message = MessagePool<TcpMessage>.Rent();
                    message.Serialize(data);
                    using (message) return TrySend(channel, message);
                }
                case ChannelKind.Unreliable:
                {
                    var message = MessagePool<UdpMessage>.Rent();
                    message.SetSessionId(_sessionId);
                    message.Serialize(data);
                    using (message) return TrySend(channel, message);
                }
                default:
                    throw new Exception($"Unknown channel: {channel}");
            }
        }

        private bool TrySend(ChannelKind channel, IMessage message)
            => _channelRouter.TrySend(channel, message);

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
            var session = _tcpNetworkSession;
            session?.Close();
            
            _readWriteBuffer = new ByteReadWriteBuffer(Config.ReceiveBufferSize);

            session = new TcpNetworkSession(this);
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
                _tcpNetworkSession = CreateNetworkSession();
                _tcpNetworkSession.Connect(RemoteEndPoint);
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
                _tcpNetworkSession = CreateNetworkSession();
                _tcpNetworkSession.Connect(RemoteEndPoint);
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
                var success = InternalSend(new C2SEngineProtocolData.SessionAuthReq
                {
                    SessionId = _sessionId,
                    PeerId = _peerId,
                    ClientPublicKey = _diffieHellman.PublicKey,
                    ClientNextExpectedSeq = _messageProcessor?.NextExpectedSeq ?? 0,
                    KeySize = _diffieHellman.KeySize,
                });
                
                if (!success && _sessionId == 0)
                {
                    throw new InvalidOperationException("Failed to send SessionAuthReq");
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
            {
                CloseWithoutHandshake();
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
            _tcpNetworkSession?.Close();
            _udpNetworkSession?.Close();
        }
        
        private void OnSessionDataReceived(object sender, DataEventArgs e)
        {
            if (!_readWriteBuffer.TryWrite(e.Data.AsSpan(e.Offset, e.Length))) return;

            try
            {
                while (_readWriteBuffer.TryRead(_policySnapshot, out var header, out var bufferOwner))
                {
                    var message = MessagePool<TcpMessage>.Rent();
                    message.Initialize(header, bufferOwner);
                    MessageReceived(message);
                }
            }
            catch (Exception ex)
            {
                OnError(ex);
                Close();
            }
        }

        private void CheckAndFlushPeriodAck()
        {
            if (!IsConnected) return;
                    
            var ackNumber = _messageProcessor.NextExpectedSeq;
            if (ackNumber <= _lastSentAck) return;
            
            var nowUtc = DateTime.UtcNow;
            var elapsedMs = (nowUtc - _lastAckTime).TotalMilliseconds;
            var pendingCount = ackNumber - _lastSentAck;

            if (elapsedMs < _messageProcessor.MaxAckDelayMs && pendingCount < _messageProcessor.AckFrequency)
                return;
                
            _lastAckTime = nowUtc;
            _lastSentAck = ackNumber;
            SendMessageAck(ackNumber);
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
            _channelRouter.Bind(new ReliableChannel(_tcpNetworkSession));
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
                    _messageProcessor?.Dispose();
                    CancelTimer();

                    StopUdpHandshakeTimer();
                    
                    while (_messageReceivedQueue.TryDequeue(out var message)) message.Dispose();
                    _messageReceivedQueue.Clear();
                    
                    _udpNetworkSession?.Close();
                    _udpNetworkSession = null;

                    _peerId = 0;
                    _sessionId = 0;
                    _tcpNetworkSession = null;

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

        private void OnUdpSocketClosed(object sender, EventArgs e)
        {
            StopFragmentAssemblerCleanupTimer();
            _channelRouter.Unbind(ChannelKind.Unreliable);
        }

        private void OnUdpSocketError(object sender, ErrorEventArgs e)
        {
            var ex = e.GetException();
            OnError(ex);
        }

        private void OnUdpSocketDataReceived(object sender, DataEventArgs e)
        {
            if (_udpNetworkSession == null) return;

            var span = e.Data.AsSpan(e.Offset, e.Length);
            if (!UdpHeader.TryRead(span, out var header, out var headerConsumed)) return;

            var buffer = BufferOwnerPool.Rent(headerConsumed + header.PayloadLength);
            span.CopyTo(buffer.Memory.Span);
            
            try
            {
                if (header.IsFragmented)
                {
                    if (_fragmentAssembler.TryProcessFragment(header, buffer, out var message))
                    {
                        MessageReceived(message);
                    }
                }
                else
                {
                    var message = MessagePool<UdpMessage>.Rent();
                    message.Initialize(header, buffer);
                    MessageReceived(message);
                }
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
                buffer.Dispose();
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
            InternalSend(new C2SEngineProtocolData.UdpHelloReq
            {
                SessionId = _sessionId,
                PeerId = PeerId,
                Mtu = Config.UdpMtu
            });
        }
        
        internal void SetServerTimeOffset(long offset)
        {
            _serverTimeOffsetFilter.Update(offset);
            _baseServerTimeOffsetMs = (long)_serverTimeOffsetFilter.Value;
        }

        internal void SetRttMs(uint rttMs)
        {
            _messageProcessor?.AddRtoSample(rttMs);
            LatencyStats.OnReceived(rttMs);
        }

        internal void SetReliableMessageProcessor(ReliableMessageProcessor processor)
        {
            _messageProcessor = processor;
        }

        internal void SetupEncryptor(byte[] serverPublicKey)
        {
            var sharedKey = _diffieHellman.DeriveSharedKey(serverPublicKey);
            _encryptor = new AesGcmEncryptor(sharedKey);
        }

        internal void SetupCompressor(int maxPayloadLength)
        {
            _compressor = new Lz4Compressor(maxPayloadLength);
        }

        internal void SetupPolicy(bool useEncrypt, bool useCompress, int compressionThreshold, int maxPayloadLength)
        {
            var g = new PolicyGlobals(useEncrypt, useCompress, compressionThreshold, maxPayloadLength);
            var snapshot = CreatePolicySnapshot(g);
            Interlocked.Exchange(ref _policySnapshot, snapshot);
        }
        
        internal bool ConnectUdpSocket(int openPort)
        {
            if (openPort <= 0) return false;
            
            var ns = new UdpNetworkSession(this);
            ns.Error += OnUdpSocketError;
            ns.DataReceived += OnUdpSocketDataReceived;
            ns.Closed += OnUdpSocketClosed;

            if (!(RemoteEndPoint is IPEndPoint ep)) return false;
            
            if (!ns.Connect(ep.Address.ToString(), openPort))
            {
                Logger.Error("Failed to connect to UDP socket. ip={0}, port={1}", ep.Address, openPort);
                return false;
            }
            
            _udpNetworkSession = ns;
            _channelRouter.Bind(new UnreliableChannel(ns));
            StartUdpHandshakeTimer();
            return true;
        }

        internal void SetupFragmentAssembler(int cleanupIntervalSec, int cleanupTimeoutSec, int pendingMessageThreshold)
        {
            if (_fragmentAssembler != null) return;
            StartFragmentAssemblerCleanupTimer(cleanupIntervalSec);
            _fragmentAssembler = new FragmentAssembler(cleanupTimeoutSec, pendingMessageThreshold);
        }

        internal void HandleRemoteAck(uint remoteAckNumber)
        {
            _messageProcessor?.AcknowledgeInFlight(remoteAckNumber);
        }

        internal void SessionAuthCompleted(long sessionId, uint peerId)
        {
            SetState(NetPeerState.Open);

            _sessionId = sessionId;
            _peerId = peerId;

            // 핑 타이머 시작
            StartPingTimer();
            Connected?.Invoke(this, EventArgs.Empty);
        }

        internal void UdpHandshakeFailed()
        {
            StopUdpHandshakeTimer();
            _udpNetworkSession?.Close();
        }

        internal void UdpHandshakeCompleted(ushort mtu)
        {
            StopUdpHandshakeTimer();
            _channelRouter.SetUdpAvailable(true);
            _udpNetworkSession.SetMaxFragmentSize(mtu);
        }

        internal bool EnableUdp()
        {
            if (_channelRouter.IsUdpAvailable) return false;
            _channelRouter.SetUdpAvailable(true);
            return true;
        }

        internal bool DisableUdp()
        {
            if (!_channelRouter.IsUdpAvailable) return false;
            _channelRouter.SetUdpAvailable(false);
            return true;
        }

        private void StartUdpHandshakeTimer()
        {
            _udpHandshakeTimer = new TickTimer(_ =>
            {
                var count = Interlocked.Increment(ref _udpHandshakeCount);
                if (count >= 3)
                {
                    StopUdpHandshakeTimer();
                    _channelRouter.SetUdpAvailable(false);
                    Logger.Error("UDP handshake failed (timed out: {0} sec).", Config.UdpHandshakeTimeSec * count);
                }
                
                SendUdpHandshake();
            }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(Config.UdpHandshakeTimeSec));
        }

        private void StopUdpHandshakeTimer()
        {
            Interlocked.Exchange(ref _udpHandshakeCount, 0);
            _udpHandshakeTimer?.Dispose();
            _udpHandshakeTimer = null;
        }

        private void StartFragmentAssemblerCleanupTimer(int periodSec)
        {
            _fragmentAssemblerCleanupTimer = new TickTimer(_ =>
            {
                _fragmentAssembler.Cleanup(DateTime.UtcNow);
            }, null, TimeSpan.FromSeconds(periodSec), TimeSpan.FromSeconds(periodSec));
        }

        private void StopFragmentAssemblerCleanupTimer()
        {
            _fragmentAssemblerCleanupTimer?.Dispose();
            _fragmentAssemblerCleanupTimer = null;
        }

        private void OnOffline()
        {
            Offline?.Invoke(this, EventArgs.Empty);
            
            // UDP 타이머 해제
            StopFragmentAssemblerCleanupTimer();
            
            // UDP 연결 해제
            _udpNetworkSession?.Close();
            _udpNetworkSession = null;
            
            // 전송중인 메시지들 초기화
            _messageProcessor?.ResetInFlightMessages();
            
            // 재연결 시작
            StartReconnection();
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_disposed)
                return;

            if (disposing)
            {
                var ns = _tcpNetworkSession;
                _tcpNetworkSession = null;
                
                if (null != ns)
                {
                    ns.Opened -= OnSessionOpened;
                    ns.Closed -= OnSessionClosed;
                    ns.Error -= OnSessionError;
                    ns.DataReceived -= OnSessionDataReceived;

                    if (ns.IsConnected)
                        ns.Close();
                }

                StopUdpHandshakeTimer();
                StopFragmentAssemblerCleanupTimer();
                
                if (_udpNetworkSession != null)
                {
                    _udpNetworkSession.DataReceived -= OnUdpSocketDataReceived;
                    _udpNetworkSession.Error -= OnUdpSocketError;
                    _udpNetworkSession.Close();
                    _udpNetworkSession = null;
                }
                
                _fragmentAssembler.Dispose();
                _diffieHellman.Dispose();
                _messageProcessor?.Dispose();
                
                CancelTimer();
            }

            _disposed = true;
        }
    }
}
