using System;
using System.Net;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SP.Common.Buffer;
using SP.Common.Logging;
using SP.Engine.Client.ProtocolHandler;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Client
{
    public class MessageEventArgs : EventArgs
    {
        public MessageEventArgs(IMessage message)
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
        Closed = 6,
    }

    public sealed class NetPeer : ReliableMessageProcessor, IDisposable
    {
        private int _stateCode;
        private bool _disposed;
        private TcpNetworkSession _session;
        private UdpSocket _udpSocket;
        private int _reconnectAttempts;
        private TickTimer _timer;
        private readonly BinaryBuffer _receiveBuffer = new BinaryBuffer();
        private readonly ConcurrentQueue<IMessage> _sendingMessageQueue = new ConcurrentQueue<IMessage>();
        private readonly ConcurrentQueue<IMessage> _receivedMessageQueue = new ConcurrentQueue<IMessage>();
        private readonly Dictionary<EProtocolId, IHandler<NetPeer, IMessage>> _engineHandlerDict =
            new Dictionary<EProtocolId, IHandler<NetPeer, IMessage>>();

        private readonly LatencyStats _latencyStats = new LatencyStats();
        private double _lastRttMs;
        private double _serverTimeOffsetMs;
        private int _connectAttempts;

        public long LastSendPingTime { get; private set; }
        public DiffieHellman DiffieHelman { get; } = new DiffieHellman(DhKeySize.Bit2048);
        public EndPoint RemoteEndPoint { get; private set; }
        public string SessionId { get; private set; }
        public EPeerId PeerId { get; private set; }
        public PackOptions PackOptions { get; }
        public ILogger Logger { get; }

        public ENetPeerState State => (ENetPeerState)_stateCode;
        public bool IsConnected => State.HasFlag(ENetPeerState.Open);

        public double SmoothedRttMs => _latencyStats.SmoothedRttMs;
        public double AverageRttMs => _latencyStats.AvgRttMs;
        public double MinRttMs => _latencyStats.MinRttMs;
        public double MaxRttMs => _latencyStats.MaxRttMs;
        public double JitterMs => _latencyStats.JitterMs;
        public float PacketLossRate => _latencyStats.PacketLossRate;

        /// <summary>
        /// 자동 핑 활성화 여부
        /// </summary>
        public bool IsEnableAutoSendPing { get; set; } = true;
        /// <summary>
        /// 자동 핑 간격(초)
        /// </summary>
        public int AutoSendPingIntervalSec { get; set; } = 30;
        /// <summary>
        /// 수신 가능한 최대 허용 크기(바이트)
        /// </summary>
        public int MaxAllowedLength { get; set; } = 4096;
        /// <summary>
        /// 재연결 최대 시도 횟수
        /// </summary>
        public int MaxReconnectAttempts { get; set; } = 10;
        /// <summary>
        /// 재연결 시도 간격(초)
        /// </summary>
        public int ReconnectIntervalSec { get; set; } = 30;
        /// <summary>
        /// MTU 값
        /// </summary>
        public ushort UdpMtu { get; set; } = 1400;
        /// <summary>
        /// 최초 연결 최대 시도 횟수
        /// </summary>
        public int MaxConnectAttempts { get; set; } = 5;
        /// <summary>
        /// 최초 연결 시도 간격(초)
        /// </summary>
        public int ConnectIntervalSec { get; set; } = 2;


        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler Offline;
        public event EventHandler<ErrorEventArgs> Error;
        public event EventHandler<MessageEventArgs> MessageReceived;
        public event EventHandler<StateChangedEventArgs> StateChanged;
        

        public NetPeer(ILogger logger)
        {
            Logger = logger;
            PackOptions = PackOptions.Default;
            _engineHandlerDict[EngineProtocol.S2C.SessionAuthAck] = new SessionAuth();
            _engineHandlerDict[EngineProtocol.S2C.Close] = new Close();
            _engineHandlerDict[EngineProtocol.S2C.MessageAck] = new MessageAck();
            _engineHandlerDict[EngineProtocol.S2C.Pong] = new Pong();
            _engineHandlerDict[EngineProtocol.S2C.UdpHelloAck] = new UdpHelloAck();
        }

        ~NetPeer()
        {
            Dispose(false);
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
            _udpSocket?.Tick();

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

            foreach (var message in DequeuePendingSend())
                EnqueueSendingMessage(message);
        }

        public bool Send(IProtocolData data)
        {
            var transport = TransportHelper.Resolve(data);
            switch (transport)
            {
                case ETransport.Tcp:
                {
                    var message = new TcpMessage();
                    if (data.ProtocolId.IsEngineProtocol())
                    {
                        message.Pack(data, null, null);
                    }
                    else
                    {
                        message.SetSequenceNumber(GetNextSequenceNumber());
                        message.Pack(data, DiffieHelman.SharedKey, PackOptions);
                    }

                    return Send(message);
                }
                case ETransport.Udp:
                {
                    var message = new UdpMessage();
                    if (data.ProtocolId.IsEngineProtocol())
                    {
                        message.SetPeerId(PeerId);
                        message.Pack(data, null, null);
                    }
                    else
                    {
                        message.SetPeerId(PeerId);
                        message.Pack(data, DiffieHelman.SharedKey, PackOptions);
                    }

                    return Send(message);
                }
                default:
                    throw new Exception($"Unknown transport: {transport}");
            }
        }

        public void SendPing()
        {
            var now = GetCurrentTimeMs();
            var ping = new EngineProtocolData.C2S.Ping
            {
                SendTimeMs = now,
                RawRttMs = _lastRttMs,
                AvgRttMs = _latencyStats.AvgRttMs,
                JitterMs = _latencyStats.JitterMs,
                PacketLossRate = _latencyStats.PacketLossRate
            };

            try
            {
                Send(ping);
                _latencyStats.OnPingSent();
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
            if (!IsEnableAutoSendPing)
                return;

            SetTimer(_ => SendPing(), null, 0, AutoSendPingIntervalSec * 1000);
        }

        private void StartConnection()
        {
            SetState(ENetPeerState.Connecting);

            var session = _session;
            session?.Close();

            session = new TcpNetworkSession();
            session.Opened += OnSessionOpened;
            session.Closed += OnSessionClosed;
            session.Error += OnSessionError;
            session.DataReceived += OnSessionDataReceived;
            _session = session;

            _connectAttempts = 0;
            SetTimer(OnConnectTimerTick, null, 0, ConnectIntervalSec * 1000);
        }

        private void OnConnectTimerTick(object state)
        {
            _connectAttempts++;
            if (_connectAttempts > MaxConnectAttempts)
            {
                Logger?.Warn("Max connect attempts exceeded");
                Close();
                return;
            }

            Logger?.Debug("Connecting...#{0}", _connectAttempts);
            
            try
            {
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

            _reconnectAttempts = 0;
            SetTimer(OnReconnectTimerTick, null, 0, ReconnectIntervalSec * 1000);
        }

        private void OnReconnectTimerTick(object state)
        {
            _reconnectAttempts++;
            if (_reconnectAttempts > MaxReconnectAttempts)
            {
                Logger?.Warn("Max reconnect attempts exceeded.");
                Close();
                return;
            }

            Logger?.Debug("Reconnecting...#{0}", _reconnectAttempts);
            
            try
            {
                _session.Connect(RemoteEndPoint);
            }
            catch (Exception e)
            {
                OnError(e);
            }
        }

        private bool Send(IMessage message)
        {
            switch (message)
            {
                case TcpMessage tcpMessage:
                {
                    if (tcpMessage.ProtocolId.IsEngineProtocol())
                        return _session.Send(tcpMessage);

                    if (!IsConnected)
                    {
                        EnqueuePendingSend(tcpMessage);
                        return true;
                    }

                    StartSendingMessage(tcpMessage);
                    if (!_session.Send(tcpMessage))
                        return false;

                    break;
                }
                case UdpMessage udpMessage:
                {
                    if (!_udpSocket?.Send(udpMessage) ?? false)
                        return false;

                    break;
                }
                default:
                    return false;
            }

            return true;
        }

        private void EnqueueSendingMessage(IMessage message)
        {
            _sendingMessageQueue.Enqueue(message);
        }

        private void SendAuthHandshake()
        {
            Send(new EngineProtocolData.C2S.SessionAuthReq
            {
                SessionId = SessionId,
                PeerId = PeerId,
                ClientPublicKey = DiffieHelman.PublicKey,
                KeySize = DiffieHelman.KeySize,
                UdpMtu = UdpMtu
            });
        }

        private void SendCloseHandshake()
        {
            Send(new EngineProtocolData.C2S.Close());
        }

        private void SendMessageAck(long sequenceNumber)
        {
            Send(new EngineProtocolData.C2S.MessageAck { SequenceNumber = sequenceNumber });
        }

        internal void CloseWithoutHandshake()
        {
            _session?.Close();
            _udpSocket?.Close();
        }

        public void Close()
        {
            if (TrySetState(ENetPeerState.None, ENetPeerState.Closing))
            {
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
                        foreach (var msg in ProcessReceivedMessage(message))
                            OnMessageReceived(msg);

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
            // 종료 중이면 메시지를 보내지 않음
            if (State == ENetPeerState.Closing  || State == ENetPeerState.Closed)
                return;

            var count = 0;
            while (_sendingMessageQueue.TryDequeue(out var message))
            {
                Send(message);
                if (++count >= 20)
                    break;
            }
        }

        private void CheckReSendMessages()
        {
            // 연결 상태일때만 체크함
            if (!IsConnected)
                return;

            foreach (var message in GetResendCandidates())
                EnqueueSendingMessage(message);
        }

        private void OnSessionDataReceived(object sender, DataEventArgs e)
        {
            var span = e.Data.AsSpan(e.Offset, e.Length);
            _receiveBuffer.Write(span);

            try
            {
                foreach (var message in Filter())
                {
                    try
                    {
                        if (_engineHandlerDict.TryGetValue(message.ProtocolId, out var handler))
                            handler.ExecuteMessage(this, message);
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
                }
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
            finally
            {
                if (_receiveBuffer.RemainSize < 1024)
                    _receiveBuffer.Trim();
            }
        }
        
        private IEnumerable<TcpMessage> Filter() 
        {
            while (true)
            {
                if (_receiveBuffer.RemainSize < TcpHeader.HeaderSize)
                    yield break;
                
                var headerSpan = _receiveBuffer.Peek(TcpHeader.HeaderSize);
                if (!TcpHeader.TryParse(headerSpan, out var header))
                    yield break;
                
                if (header.PayloadLength > MaxAllowedLength)
                {
                    Logger.Warn("Max allowed length. maxAllowedLength={0}, payloadLength={1}", MaxAllowedLength, header.PayloadLength);
                    Close();
                    yield break;
                }

                var payloadLength = header.Length + header.PayloadLength;
                if (_receiveBuffer.RemainSize < payloadLength)
                    yield break;

                var payload = _receiveBuffer.ReadBytes(payloadLength);
                var message = new TcpMessage(header, new ArraySegment<byte>(payload));
                yield return message;
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
            // 인증 시작
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

        internal void OnError(EEngineErrorCode errorCode)
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
            var socket = new UdpSocket();
            socket.Error += OnUdpSocketError;
            socket.DataReceived += OnUdpSocketDataReceived;

            var ipAddress = ((IPEndPoint)RemoteEndPoint).Address;
            if (!socket.Connect(ipAddress.ToString(), port, UdpMtu))
            {
                Logger.Error("Failed to connect to UDP socket. ip={0}, port={1}", ipAddress.ToString(), port);
                return;
            }

            _udpSocket = socket;
            SendUdpHandshake();
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
                if (_engineHandlerDict.TryGetValue(message.ProtocolId, out var handler))
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
            Send(new EngineProtocolData.C2S.UdpHelloReq
            {
                SessionId = SessionId,
                PeerId = PeerId
            });
        }

        internal void OnOpened(EPeerId peerId, string sessionId, int udpPort)
        {
            SetState(ENetPeerState.Open);

            PeerId = peerId;
            SessionId = sessionId;
            
            // 핑 타이머 시작
            StartPingTimer();
            
            ConnectUdpSocket(udpPort);
            Connected?.Invoke(this, EventArgs.Empty);
        }

        internal void OnUdpHelloAck(EEngineErrorCode errorCode)
        {
            if (errorCode != EEngineErrorCode.Success)
            {
                _udpSocket.Close();
                _udpSocket = null;
                return;
            }
            
            Logger.Debug("Udp socket connected");
        }
        
        private void OnOffline()
        {
            ResetProcessorState();
            Offline?.Invoke(this, EventArgs.Empty);
            StartReconnection();
        }
        
        private void OnMessageReceived(IMessage message)
        {
            MessageReceived?.Invoke(this, new MessageEventArgs(message));
        }

        internal void OnPong(long sendTimeMs, long serverTimeMs)
        {
            var now = GetCurrentTimeMs();
            _lastRttMs = now - sendTimeMs;
            _latencyStats.OnPongReceived(_lastRttMs);
            RecordRttSample(_lastRttMs);

            var estimatedServerTime = serverTimeMs + _lastRttMs / 2.0;
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

                _latencyStats.Clear();
                CancelTimer();
                ResetProcessorState();
            }

            _disposed = true;
        }
    }
}
