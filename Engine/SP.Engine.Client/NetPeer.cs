using System;
using System.Net;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SP.Common;
using SP.Common.Buffer;
using SP.Common.Logging;
using SP.Engine.Client.ProtocolHandler;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Message;
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

    public enum ENetPeerState
    {
        None = 0,
        Connecting = 1,
        Open = 2,
        Closing = 3,
        Closed = 4,
    }

    public sealed class NetPeer : MessageProcessor, IDisposable
    {
        private sealed class Timer : IDisposable
        {
            private DateTime _lastExecutionTime;
            private readonly int _dueTimeMs;
            private readonly int _intervalMs;
            private readonly object _state;
            private Action<object> _callback;
            private bool _isRunning;
            private bool _isFirstExecution;

            public Timer(Action<object> callback, object state, int dueTimeMs, int intervalMs)
            {
                _isRunning = true;
                _isFirstExecution = true;
                _lastExecutionTime = DateTime.UtcNow;
                _callback = callback;
                _state = state;
                _dueTimeMs = dueTimeMs;
                _intervalMs = intervalMs;
            }

            public void Update()
            {
                if (!_isRunning) return;

                var now = DateTime.UtcNow;
                var elapsedMs = (int)(now - _lastExecutionTime).TotalMilliseconds;

                if (_isFirstExecution)
                {
                    if (Timeout.Infinite == _dueTimeMs || elapsedMs < _dueTimeMs) return;
                    _callback?.Invoke(_state);
                    _lastExecutionTime = now;
                    _isFirstExecution = false;

                    if (_intervalMs == Timeout.Infinite)
                        Dispose();
                }
                else
                {
                    if (Timeout.Infinite == _intervalMs || elapsedMs < _intervalMs) return;
                    _callback?.Invoke(_state);
                    _lastExecutionTime = now;
                }
            }

            public void Dispose()
            {
                _isRunning = false;
                _callback = null;
            }
        }

        private int _stateCode;
        private bool _disposed;
        private string _ip;
        private int _port;
        private TcpNetworkSession _session;
        private UdpSocket _udpSocket;
        private DateTime _serverUpdateTime;
        private DateTime _lastServerTime;
        private int _reconnectionAttempts;
        private Timer _timer;
        private bool _isReconnecting;
        private readonly DataSampler<int> _latencySampler = new DataSampler<int>(1024);
        private readonly DiffieHellman _diffieHelman = new DiffieHellman(DhKeySize.Bit2048);
        private readonly BinaryBuffer _receiveBuffer = new BinaryBuffer();
        private readonly ConcurrentQueue<IMessage> _sendingMessageQueue = new ConcurrentQueue<IMessage>();
        private readonly ConcurrentQueue<IMessage> _receivedMessageQueue = new ConcurrentQueue<IMessage>();

        private readonly Dictionary<EProtocolId, IHandler<NetPeer, IMessage>> _engineHandlerDict =
            new Dictionary<EProtocolId, IHandler<NetPeer, IMessage>>();
        
        public EndPoint RemoteEndPoint { get; private set; }
        public EPeerId PeerId { get; private set; }   
        public DateTime LastSendPingTime { get; private set; }
        public int AvgLatencyMs => (int)_latencySampler.Avg;
        public int LatencyStdDevMs => (int)_latencySampler.StdDev;
        public ENetPeerState State => (ENetPeerState)_stateCode;
        public byte[] DhSharedKey => _diffieHelman.SharedKey;
        public string SessionId { get; private set; }
        public bool IsConnected => State.HasFlag(ENetPeerState.Open);

        public PackOptions PackOptions { get; }
        
        public DateTime ServerTime
        {
            get
            {
                var elapsedTimeMs = Math.Max(0, (int)Math.Round(DateTime.UtcNow.Subtract(_serverUpdateTime).TotalMilliseconds, MidpointRounding.AwayFromZero));
                return _lastServerTime.AddMilliseconds(elapsedTimeMs);
            }
        }        
        
        public bool IsEnableAutoSendPing { get; set; } = true;
        public int AutoSendPingIntervalSec { get; set; } = 30;
        public int MaxAllowedLength { get; set; } = 4096;
        public int MaxConnectionAttempts { get; set; } = 5;
        public int ReconnectionIntervalSec { get; set; } = 30;
        public ushort UdpMtu { get; set; } = 1400;
        
        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler Offline;
        public event EventHandler<ErrorEventArgs> Error;
        public event EventHandler<MessageEventArgs> MessageReceived;
        public event Action<ENetPeerState> StateChanged;
        
        public ILogger Logger { get; }
        
        public NetPeer(ILogger logger = null)
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

        private bool TrySetState(ENetPeerState expected, ENetPeerState next)
        {
            if (Interlocked.CompareExchange(ref _stateCode, (int)next, (int)expected) != (int)expected) return false;
            Logger?.Info($"[NetPeer] State changed: {expected} -> {next}");
            StateChanged?.Invoke(next);
            return true;
        }

        private void SetState(ENetPeerState state)
        {
            Interlocked.Exchange(ref _stateCode, (int)state);
            Logger?.Info($"[NetPeer] State changed: {state}");
            StateChanged?.Invoke(state);
        }

        private void SetTimer(Action<object> callback, object state, int dueTimeMs, int intervalMs)
        {
            _timer?.Dispose();
            _timer = new Timer(callback, state, dueTimeMs, intervalMs);
        }
        
        private static EndPoint ResolveEndPoint(string ip, int port)
        {
            if (IPAddress.TryParse(ip, out var address))
                return new IPEndPoint(address, port);
            return new DnsEndPoint(ip, port);
        }

        public void DeriveSharedKey(byte[] serverPublicKey)    
        {
            _diffieHelman.DeriveSharedKey(serverPublicKey);
        }

        public void Open(string ip, int port)
        {
            if (null != _session)
                throw new InvalidOperationException("Already opened.");

            if (string.IsNullOrEmpty(ip) || 0 >= port)
                throw new ArgumentException("Invalid ip or port");
            
            _ip = ip;
            _port = port;
            RemoteEndPoint = ResolveEndPoint(ip, port);
            StartConnection();
        }

        private void StartConnection()
        {
            SetState(ENetPeerState.Connecting);
            
            _session?.Close();
            _session = CreateNewSession();

            try
            {
                _session.Connect(RemoteEndPoint);
            }
            catch (Exception e)
            {
                OnError(e);
                Close();
            }
        }

        private void TryReconnection(object state)
        {
            var session = (TcpNetworkSession)state;
            if (MaxConnectionAttempts > 0 && _reconnectionAttempts >= MaxConnectionAttempts)
            {
                Logger?.Warn("Max connection attempts exceeded.");
                Close();
                return;
            }

            try
            {
                _reconnectionAttempts++;
                Logger?.Info($"Reconnect attempt #{_reconnectionAttempts}");
                session.Connect(RemoteEndPoint);
            }
            catch (Exception e)
            {
                OnError(e);
                Close();
            }
        }
        
        public bool Send(IProtocolData data)
        {
            var message = PackMessage(data);
            return Send(message);
        }
        
        private IMessage PackMessage(IProtocolData data)
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
                        message.SetSequenceNumber(GetNextSendSequenceNumber());
                        message.Pack(data, _diffieHelman.SharedKey, PackOptions);
                    }
                    return message;
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
                        message.Pack(data, _diffieHelman.SharedKey, PackOptions);
                    }
                    return message;
                }
                default:
                    throw new Exception($"Unknown transport: {transport}");
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
                        EnqueuePendingMessage(tcpMessage);
                        return true;
                    }
                    
                    RegisterSendingMessage(tcpMessage);
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
        
        private void StartPingTimer()
        {
            // 자동 핑 on/off 여부
            if (!IsEnableAutoSendPing)
                return;

            var intervalSec = AutoSendPingIntervalSec > 0 ? AutoSendPingIntervalSec : 60;
            SetTimer(_ => SendPing(), null, intervalSec * 1000, intervalSec * 1000);
        }
        
        public void SendPing()
        {
            try
            {
                var protocol = new EngineProtocolData.C2S.Ping
                {
                    SendTime = DateTime.UtcNow,
                    LatencyAverageMs = AvgLatencyMs,
                    LatencyStandardDeviationMs = LatencyStdDevMs,
                };

                if (Send(protocol))
                    LastSendPingTime = protocol.SendTime;
              
            }
            catch (Exception e)
            {
                OnError(e);
            }
        }

        private void SendAuthHandshake()
        {
            Send(new EngineProtocolData.C2S.SessionAuthReq
            {
                SessionId = SessionId,
                PeerId = PeerId,
                ClientPublicKey = _diffieHelman.PublicKey,
                KeySize = _diffieHelman.KeySize,
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
            if (TrySetState(ENetPeerState.None, ENetPeerState.Closed))
            {
                // 초기 상태에서 종료를 호출함
                OnClosed();
                return;
            }

            if (TrySetState(ENetPeerState.Connecting, ENetPeerState.Closing))
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
                _session?.Close();
                _udpSocket?.Close();
            }, null, 5000, Timeout.Infinite);
        }

        private void DequeueReceivedMessage()
        {
            // 연결된 상태에서만 메시지를 처리함
            if (State != ENetPeerState.Open)
                return;
            
            while (_receivedMessageQueue.TryDequeue(out var message))
            {
                switch (message)
                {
                    case TcpMessage _:
                    {
                        // 순서대로 처리하기위해 대기 중인 메시지들을 확인함
                        var messages = DrainInOrderReceivedMessages(message);
                        foreach (var msg in messages)
                            OnMessageReceived(msg);

                        break;
                    }
                    case UdpMessage _:
                    {
                        OnMessageReceived(message);
                        break;
                    }
                }

            }
        }

        private void DequeueSendingMessage()
        {
            // 종료 중이면 메시지를 보내지 않음
            if (State == ENetPeerState.Closing  || State == ENetPeerState.Closed)
                return;

            while (_sendingMessageQueue.TryDequeue(out var message))
                Send(message);
        }

        private void CheckReSendMessages()
        {
            // 연결 상태일때만 체크함
            if (!(State is ENetPeerState.Open))
                return;
            
            foreach (var message in GetTimedOutMessages())
                EnqueueSendingMessage(message);
        }
        

        public void Tick()
        {
            _timer?.Update();
            _udpSocket?.Tick();
            
            // 수신된 메시지 처리
            DequeueReceivedMessage();

            // 전송 메시지 처리
            DequeueSendingMessage();

            // 재 전송 메시지 체크
            CheckReSendMessages();
        }

        private TcpNetworkSession CreateNewSession()
        {
            // 세션 생성 및 이벤트 연결
            var session = new TcpNetworkSession();
            session.Opened += OnSessionOpened;
            session.Closed += OnSessionClosed;
            session.Error += OnSessionError;
            session.DataReceived += OnSessionDataReceived;
            return session;
        }
        
        private UdpSocket CreateNewUdpSocket()
        {
            var socket = new UdpSocket();
            socket.Error += OnUdpSocketError;
            socket.DataReceived += OnUdpSocketDataReceived;
            return socket;
        }

        private void OnUdpSocketError(object sender, ErrorEventArgs e)
        {
            var ex = e.GetException();
            Logger.Error("UdpSocket Error: {0}", ex.Message);
        }

        private void OnUdpSocketDataReceived(object sender, DataEventArgs e)
        {
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
                if (_engineHandlerDict.TryGetValue(header.ProtocolId, out var handler))
                    handler.ExecuteMessage(this, message);
                else
                    EnqueueReceivedMessage(message);
            }
            catch (Exception ex)
            {
                OnError(ex);
            }
        }

        private void OnSessionDataReceived(object sender, DataEventArgs e)
        {
            _receiveBuffer.Write(e.Data.AsSpan(e.Offset, e.Length));

            try
            {
                foreach (var message in Filter())
                {
                    try
                    {
                        if (_engineHandlerDict.TryGetValue(message.ProtocolId, out var handler))
                        {
                            handler.ExecuteMessage(this, message);
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

                if (_receiveBuffer.RemainSize < TcpHeader.HeaderSize + header.PayloadLength)
                    yield break;

                var payload = _receiveBuffer.ReadBytes(TcpHeader.HeaderSize + header.PayloadLength);
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
            _isReconnecting = false;
            
            OnClosed();
        }

        private void OnSessionOpened(object sender, EventArgs e)
        {
            _isReconnecting = false;
            
            // 인증 요청
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
            switch ((ENetPeerState)_stateCode)
            {
                case ENetPeerState.Open:
                {
                    // 오프라인으로 전환
                    OnOffline();
                    break;
                }
                case ENetPeerState.Connecting:
                {
                    if (_isReconnecting) break;
                    _isReconnecting = true;
                    SetTimer(TryReconnection, _session, 0, ReconnectionIntervalSec * 1000);
                    break;
                }
                default:
                {
                    SetState(ENetPeerState.Closed);
                    _timer?.Dispose();
                    _session = null;
                    _udpSocket = null;
                    Disconnected?.Invoke(this, EventArgs.Empty);
                    break;   
                }
            }
        }

        private void ConnectUdpSocket(int port)
        {
            _udpSocket = CreateNewUdpSocket();
            if (!_udpSocket.Connect(this, _ip, port, UdpMtu))
            {
                Logger.Error("Failed to connect to UDP socket. ip={0}, port={1}", _ip, port);
                return;
            }

            SendUdpHandshake();
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
            _reconnectionAttempts = 0;
            
            PeerId = peerId;
            SessionId = sessionId;
            
            // 핑 타이머 시작
            StartPingTimer();
            
            // 대기 중인 메시지 전송
            var messages = GetPendingMessages();
            foreach (var message in messages)
                EnqueueSendingMessage(message);
            
            ConnectUdpSocket(udpPort);
            Connected?.Invoke(this, EventArgs.Empty);
        }

        private void OnOffline()
        {            
            // 전송 메시지 상태 초기화
            ResetAllSendingStates();
            
            // 재 연결 시작
            StartConnection();
            Offline?.Invoke(this, EventArgs.Empty);
        }
        
        private void OnMessageReceived(IMessage message)
        {
            MessageReceived?.Invoke(this, new MessageEventArgs(message));
        }

        internal void OnPong(DateTime sentTime, DateTime serverTime)
        {
            // 네트워크 레이턴시 계산
            var latencyMs = (int)Math.Round(DateTime.UtcNow.Subtract(sentTime).TotalMilliseconds, MidpointRounding.AwayFromZero);
            _latencySampler.Add(latencyMs);
            // 네트워크 레이턴시를 고려해 서버 시간 설정
            _lastServerTime = serverTime.AddMilliseconds(latencyMs / 2.0f);
            _serverUpdateTime = DateTime.UtcNow;
            //Logger.Debug("latencyMs={0}, avgMs={1}, stddevMs={2}, serverTime={3:yyyy-MM-dd hh:mm:ss.fff}", latencyMs, AvgLatencyMs, LatencyStdDevMs, _lastServerTime);
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
                    _udpSocket.Close();
                    _udpSocket = null;
                }

                _timer?.Dispose();
                _latencySampler.Clear();
                ResetMessageProcessor();
            }

            _disposed = true;
        }
        
        protected override void OnMessageSendFailure(IMessage message)
        {
            OnError(new Exception($"The message resend limit has been exceeded: {message.ProtocolId} ({message.SequenceNumber})"));
            Close();
        }
        
        protected override void OnRttSpikeDetected(long sequenceNumber, double rawRtt, double estimatedRtt)
        {
            Logger.Warn($"RTT spike detected: Seq={sequenceNumber}, Raw={rawRtt:F2}ms, Est={estimatedRtt:F2}ms");
        }
    }
}
