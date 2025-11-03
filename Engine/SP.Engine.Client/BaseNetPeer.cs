using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
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
        private readonly Dictionary<ushort, ICommand> _commands = new Dictionary<ushort, ICommand>();
        private readonly DiffieHellman _diffieHellman = new DiffieHellman(DhKeySize.Bit2048);
        private readonly Dictionary<ushort, ICommand> _internalCommands = new Dictionary<ushort, ICommand>();
        private readonly BinaryReceiveBuffer _receiveBuffer = new BinaryReceiveBuffer(1024);
        private readonly ConcurrentQueue<IMessage> _receiveQueue = new ConcurrentQueue<IMessage>();
        private Lz4Compressor _compressor;
        private bool _disposed;
        private AesCbcEncryptor _encryptor;
        private TickTimer _keepAliveTimer;
        private IPolicyView _networkPolicy = new NetworkPolicyView(in PolicyDefaults.Globals);
        private ReliableMessageProcessor _reliableMessageProcessor;
        private double _serverTimeOffsetMs;
        private TcpNetworkSession _session;
        private string _sessionId;
        private int _stateCode;
        private TickTimer _timer;
        private UdpSocket _udpSocket;

        public int ConnectTryCount { get; private set; }
        public int ReconnectTryCount { get; private set; }
        public int MaxFrameBytes { get; private set; }
        public long LastSendPingTime { get; private set; }
        public EndPoint RemoteEndPoint { get; private set; }
        public uint PeerId { get; private set; }
        public EngineConfig Config { get; private set; }
        public LatencyStats LatencyStats { get; private set; }
        public NetPeerState State => (NetPeerState)_stateCode;
        public bool IsConnected => State == NetPeerState.Open;

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
            LatencyStats = new LatencyStats(config.LatencySampleWindowSize);
            _reliableMessageProcessor = new ReliableMessageProcessor(logger);

            _internalCommands[S2CEngineProtocolId.SessionAuthAck] = new SessionAuth();
            _internalCommands[S2CEngineProtocolId.Close] = new Close();
            _internalCommands[S2CEngineProtocolId.MessageAck] = new MessageAck();
            _internalCommands[S2CEngineProtocolId.Pong] = new Pong();
            _internalCommands[S2CEngineProtocolId.UdpHelloAck] = new UdpHelloAck();

            DiscoverCommands();
            Logger.Debug("Discover commands: [{0}]", string.Join(", ", _commands.Keys));
        }

        ~BaseNetPeer()
        {
            Dispose(false);
        }

        private void DiscoverCommands()
        {
            var type = GetType();
            var assembly = GetType().Assembly;
            foreach (var t in assembly.GetTypes())
            {
                if (!t.IsClass || t.IsAbstract) continue;
                var attr = t.GetCustomAttribute<ProtocolCommandAttribute>();
                if (attr == null) continue;
                if (!typeof(ICommand).IsAssignableFrom(t)) continue;
                if (!(Activator.CreateInstance(t) is ICommand command)) continue;
                if (type != command.ContextType) continue;
                if (!_commands.TryAdd(attr.Id, command))
                    throw new Exception($"Duplicate command: {attr.Id}");
            }
        }

        private ICommand GetCommand(ushort id)
        {
            _commands.TryGetValue(id, out var command);
            return command;
        }

        public TrafficInfo GetTcpTrafficInfo()
        {
            return _session?.GetTrafficInfo();
        }

        public TrafficInfo GetUdpTrafficInfo()
        {
            return _udpSocket?.GetTrafficInfo();
        }

        private static long GetCurrentTimeMs()
        {
            return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }

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
            _udpSocket?.Tick();

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

            foreach (var message in _reliableMessageProcessor.DequeuePendingMessages())
                TrySend(ChannelKind.Reliable, message);
        }

        public bool Send(IProtocolData data)
        {
            var sequenceNumber = data.Channel == ChannelKind.Reliable
                ? _reliableMessageProcessor.GetNextReliableSeq()
                : 0;
            var policy = _networkPolicy.Resolve(data.GetType());
            var encryptor = policy.UseEncrypt ? Encryptor : null;
            var compressor = policy.UseCompress ? Compressor : null;
            return TrySend(sequenceNumber, data, policy, encryptor, compressor);
        }

        private bool InternalSend(IProtocolData data)
        {
            var policy = _networkPolicy.Resolve(data.GetType());
            var encryptor = policy.UseEncrypt ? Encryptor : null;
            var compressor = policy.UseCompress ? Compressor : null;
            return TrySend(0, data, policy, encryptor, compressor);
        }

        private bool TrySend(long sequenceNumber, IProtocolData data, IPolicy policy, IEncryptor encryptor,
            ICompressor compressor)
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
                            _reliableMessageProcessor.EnqueuePendingMessage(msg);
                            return true;
                        }

                        if (sequenceNumber > 0) _reliableMessageProcessor.RegisterMessageState(msg);
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
                    throw new Exception("Failed to send auth handshake");
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

        private void DequeueReceivedMessage()
        {
            if (!IsConnected) return;
            while (_receiveQueue.TryDequeue(out var message))
                foreach (var msg in _reliableMessageProcessor.ProcessMessageInOrder(message))
                    OnMessageReceived(msg);
        }

        private void CheckRetryMessage()
        {
            // 연결 상태일때만 체크함
            if (!IsConnected)
                return;

            // 메시지 재전송 
            if (_reliableMessageProcessor.TryGetRetryMessages(out var retries))
                foreach (var message in retries)
                    _reliableMessageProcessor.EnqueuePendingMessage(message);
            else
                // 재전송 실패인 경우 종료함
                Close();
        }

        private void OnSessionDataReceived(object sender, DataEventArgs e)
        {
            _receiveBuffer.TryWrite(e.Data, e.Offset, e.Length);

            try
            {
                foreach (var message in Filter())
                    if (_internalCommands.TryGetValue(message.Id, out var command))
                    {
                        command.Execute(this, message);
                    }
                    else
                    {
                        SendMessageAck(message.SequenceNumber);
                        EnqueueReceivedMessage(message);
                    }
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
            finally
            {
                _receiveBuffer.ResetIfConsumed();
            }
        }

        private IEnumerable<TcpMessage> Filter()
        {
            const int maxFramePerTick = 128;
            const int headerSize = TcpHeader.ByteSize;
            var frames = 0;

            while (frames < maxFramePerTick)
            {
                if (_receiveBuffer.ReadableBytes < headerSize)
                    yield break;

                var headerSpan = _receiveBuffer.ReadableSpan.Slice(0, headerSize);
                if (!TcpHeader.TryRead(headerSpan, out var header, out var consumed))
                    yield break;

                var bodyLen = header.PayloadLength;
                if (bodyLen <= 0 || bodyLen > MaxFrameBytes)
                {
                    Logger.Warn("Invalid payload length. id={0}, max={1}, len={2}",
                        header.MsdId, MaxFrameBytes, bodyLen);
                    Close();
                    yield break;
                }

                var total = consumed + bodyLen;
                if (_receiveBuffer.ReadableBytes < total)
                    yield break;

                _receiveBuffer.Consume(consumed);

                if (!_receiveBuffer.TryReadBytes(bodyLen, out var bodyBytes))
                    yield break;

                var msg = new TcpMessage(header, bodyBytes);
                yield return msg;
                frames++;
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
        ///     수신된 메시지를 큐에 넣습니다.
        /// </summary>
        private void EnqueueReceivedMessage(IMessage message)
        {
            _receiveQueue.Enqueue(message);
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
                    _reliableMessageProcessor.Reset();
                    CancelTimer();

                    StopUdpKeepAliveTimer();
                    _udpSocket?.Close();
                    _udpSocket = null;

                    PeerId = 0;
                    _sessionId = null;
                    _session = null;
                    _channelRouter.Unbind(ChannelKind.Reliable);
                    _channelRouter.Unbind(ChannelKind.Unreliable);

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
            SendUdpHandshake();
        }

        private void OnUdpSocketClosed(object sender, EventArgs e)
        {
            StopUdpKeepAliveTimer();
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

            var datagram = new byte[e.Length];
            Buffer.BlockCopy(e.Data, e.Offset, datagram, 0, datagram.Length);

            var headerSpan = datagram.AsSpan(0, UdpHeader.ByteSize);
            if (!UdpHeader.TryRead(headerSpan, out var header, out var consumed))
                return;

            if (PeerId != header.PeerId)
                return;

            var bodyOffset = consumed;
            var bodyLen = header.PayloadLength;

            IMessage message;
            if (header.Fragmented == 0x01)
            {
                var bodySpan = datagram.AsSpan(bodyOffset, bodyLen);
                if (!FragmentHeader.TryParse(bodySpan, out var fragHeader, out consumed))
                    return;

                if (bodySpan.Length < consumed + fragHeader.FragLength)
                    return;

                var fragPayload = new ArraySegment<byte>(datagram, bodyOffset + consumed, fragHeader.FragLength);

                if (!_udpSocket.Assembler.TryAssemble(header, fragHeader, fragPayload, out var assembled))
                    return;

                var normalizedHeader = new UdpHeaderBuilder()
                    .From(header)
                    .WithPayloadLength(assembled.Count)
                    .Build();

                message = new UdpMessage(normalizedHeader, assembled);
            }
            else
            {
                var payload = new ArraySegment<byte>(datagram, bodyOffset, bodyLen);
                var normalizedHeader = new UdpHeaderBuilder()
                    .From(header)
                    .WithPayloadLength(payload.Count)
                    .Build();

                message = new UdpMessage(normalizedHeader, payload);
            }

            try
            {
                if (_internalCommands.TryGetValue(message.Id, out var command))
                    command.Execute(this, message);
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
            if (!InternalSend(new C2SEngineProtocolData.UdpHelloReq
                {
                    SessionId = _sessionId,
                    PeerId = PeerId,
                    Mtu = Config.UdpMtu
                }))
                throw new Exception("Failed to send UDP handshake");
        }

        internal void OnAuthHandshake(S2CEngineProtocolData.SessionAuthAck p)
        {
            if (p.Result != SessionHandshakeResult.Ok)
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
            if (p.SendTimeoutMs > 0) _reliableMessageProcessor.SetSendTimeoutMs(p.SendTimeoutMs);
            if (p.MaxRetryCount > 0) _reliableMessageProcessor.SetMaxRetryCount(p.MaxRetryCount);

            if (p.UseEncrypt)
            {
                var sharedKey = _diffieHellman.DeriveSharedKey(p.ServerPublicKey);
                _encryptor = new AesCbcEncryptor(sharedKey);
            }

            if (p.UseCompress) _compressor = new Lz4Compressor(p.MaxFrameBytes);

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

        internal void OnUdpHandshake(S2CEngineProtocolData.UdpHelloAck p)
        {
            if (p.Result != UdpHandshakeResult.Ok)
            {
                _udpSocket.Close();
                _udpSocket = null;
                _channelRouter.Unbind(ChannelKind.Unreliable);
                Logger.Error("UDP handshake failed: {0}", p.Result);
                return;
            }

            _udpSocket.SetMaxFrameSize(p.Mtu);
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
            _reliableMessageProcessor.ResetAllMessageStates();
            Offline?.Invoke(this, EventArgs.Empty);

            StopUdpKeepAliveTimer();
            _udpSocket?.Close();
            _udpSocket = null;

            _channelRouter.Unbind(ChannelKind.Unreliable);
            StartReconnection();
        }

        private void OnMessageReceived(IMessage message)
        {
            var command = GetCommand(message.Id);
            if (command == null)
            {
                Logger.Warn("Not found command: {0}", message.Id);
                return;
            }

            command.Execute(this, message);
        }

        internal void OnPong(long sendTimeMs, long serverTimeMs)
        {
            var now = GetCurrentTimeMs();
            var rttMs = now - sendTimeMs;
            LatencyStats.OnReceived(rttMs);
            _reliableMessageProcessor.AddRtoSample(rttMs);

            var estimatedServerTime = serverTimeMs + rttMs / 2.0;
            _serverTimeOffsetMs = estimatedServerTime - now;
        }

        internal void OnMessageAck(long sequenceNumber)
        {
            _reliableMessageProcessor.RemoveMessageState(sequenceNumber);
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
                PeerId = 0;
                LatencyStats.Clear();
                CancelTimer();
                _reliableMessageProcessor.Reset();
            }

            _disposed = true;
        }
    }
}
