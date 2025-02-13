using System;
using System.Net;
using System.Reflection;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using SP.Engine.Core;
using SP.Engine.Core.Message;
using SP.Engine.Core.Protocol;
using SP.Engine.Core.Utility;

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
        None = NetPeerStateConst.None,
        Connecting = NetPeerStateConst.Connecting,
        Open = NetPeerStateConst.Open,
        Closing = NetPeerStateConst.Closing,
        Closed = NetPeerStateConst.Closed,
    }

    public static class NetPeerStateConst
    {
        public const int None = 0;
        public const int Connecting = 1;
        public const int Open = 2;
        public const int Closing = 3;
        public const int Closed = 4;
    }
    
    public sealed class NetPeer : MessageProcessor, IProtocolHandler, IDisposable
    {
        private class TimerManager
        {
            private class Timer
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
            
            private Timer _timer;
            public void SetTimer(Action<object> callback, object state, int dueTimeMs, int intervalMs)
            {
                _timer?.Dispose();
                _timer = new Timer(callback, state, dueTimeMs, intervalMs);
            }

            public void Update()
            {
                _timer?.Update();
            }

            public void Dispose()
            {
                _timer?.Dispose();
                _timer = null;
            }
        }

        private string _sessionId;
        private int _stateCode;
        private bool _disposed;
        private ServerSession _session;
        private DateTime _serverUpdateTime;
        private DateTime _lastServerTime;
        private int _connectionAttempts;
        private readonly TimerManager _timer = new TimerManager(); 
        private readonly DataSampler<int> _latencySampler = new DataSampler<int>(1024);
        private readonly DiffieHellman _diffieHellman = new DiffieHellman(ECryptographicKeySize.KS256);
        private readonly MessageFilter _messageFilter = new MessageFilter();
        private readonly ConcurrentQueue<IMessage> _sendingMessageQueue = new ConcurrentQueue<IMessage>();
        private readonly ConcurrentQueue<IMessage> _receivedMessageQueue = new ConcurrentQueue<IMessage>();
        
        public EndPoint RemoteEndPoint { get; private set; }
        public EPeerId PeerId { get; private set; }   
        public DateTime LastSendPingTime { get; private set; }
        public int AvgLatencyMs => (int)_latencySampler.Mean;
        public int LatencyStddevMs => (int)_latencySampler.StandardDeviation;
        public ENetPeerState State => (ENetPeerState)_stateCode;
        public byte[] CryptoSharedKey => _diffieHellman.SharedKey;

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
        public int LimitRequestLength { get; set; } = 4096;
        public int MaxConnectionAttempts { get; set; } = 5;
        public int ReconnectionIntervalSec { get; set; } = 30;
        
        public event EventHandler Connected;
        public event EventHandler Disconnected;
        public event EventHandler Offline;
        public event EventHandler<ErrorEventArgs> Error;
        public event EventHandler<MessageEventArgs> MessageReceived;
        
        public NetPeer(Assembly assembly)
        {
            var assemblies = new List<Assembly>
            {
                assembly,
                Assembly.GetExecutingAssembly()
            };

            ProtocolManager.Initialize(assemblies, e => throw new Exception($"Failed to load protocols. exception={e.Message}\r\nstackTrace={e.StackTrace}"));
        }

        ~NetPeer()
        {
            Dispose(false);
        }
        
        private static EndPoint ResolveEndPoint(string host, int port)
        {
            if (IPAddress.TryParse(host, out var ipAddress))
                return new IPEndPoint(ipAddress, port);
            return new DnsEndPoint(host, port);
        }

        public void Open(string host, int port)
        {
            if (null != _session)
                throw new Exception("Already opened.");

            if (string.IsNullOrEmpty(host) || 0 >= port)
                throw new ArgumentException("Invalid host or port");
            
            RemoteEndPoint = ResolveEndPoint(host, port);
            
            // 연결 시작
            StartConnection();
        }

        private void StartConnection()
        {
            // 연결 중 상태 변경
            _stateCode = NetPeerStateConst.Connecting;

            var session = _session;
            session?.Close();
            
            // 새로운 세션 생성
            session = CreateNewSession();
            _session = session;

            try
            {
                _timer.SetTimer(s =>
                {
                    try
                    {
                        if (0 < MaxConnectionAttempts && MaxConnectionAttempts <= _connectionAttempts)
                            throw new Exception("Max connection attempts exceeded.");

                        _connectionAttempts++;
                        if (s is ServerSession ss)
                            ss.Connect(RemoteEndPoint);   
                    }
                    catch (Exception e)
                    {
                        OnError(e);
                        Close();
                    }
                }, session, 100, ReconnectionIntervalSec * 1000);
            }
            catch (Exception e)
            {
                OnError(e);
            }
        }

        public void Send(IProtocolData protocol)
        {
            try
            {
                if (protocol.ProtocolId.IsEngineProtocol())
                {
                    var message = new TcpMessage();
                    message.SerializeProtocol(protocol, null);
                    var bytes = message.ToArray();
                    if (bytes != null && bytes.Length > 0)
                        _session?.TrySend(bytes, 0, bytes.Length);
                }
                else
                {
                    var message = new TcpMessage();
                    message.SerializeProtocol(protocol, CryptoSharedKey);
                    EnqueueSendingMessage(message);   
                }
            }
            catch (Exception ex)
            {
                OnError(ex);
            }            
        }
        
        private void Send(IMessage message)
        {
            var session = _session;
            if (null == session)
                return;

            switch (State)
            {
                case ENetPeerState.Connecting:
                    // 대기 메시지 등록
                    // 연결이 완료되면 전송함
                    RegisterPendingMessage(message);
                    break;
                case ENetPeerState.Open:
                {
                    // 전송 메시지 상태 등록
                    AddSendingMessage(message);   

                    var data = message.ToArray();
                    if (null != data)
                    {
                        try
                        {
                            if (!session.TrySend(data, 0, data.Length))
                                throw new Exception($"Failed to send message. protocolId={message.ProtocolId}");
                        }
                        catch (Exception ex)
                        {
                            OnError(ex);
                        }
                    }
                }
                    break;
                case ENetPeerState.Closing: 
                case ENetPeerState.Closed:
                    break;
            }
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
            _timer.SetTimer(_ => SendPing(), null, intervalSec * 1000, intervalSec * 1000);
        }
        
        public void SendPing()
        {
            try
            {
                var now = DateTime.UtcNow;
                var notifyPingInfo = new EngineProtocolDataC2S.NotifyPingInfo
                {
                    SendTime = now,
                    LatencyAverageMs = AvgLatencyMs,
                    LatencyStandardDeviationMs = LatencyStddevMs,
                };

                LastSendPingTime = now;
                Send(notifyPingInfo);
            }
            catch (Exception e)
            {
                OnError(e);
            }
        }

        private void SendAuthHandshake()
        {
            Send(new EngineProtocolDataC2S.AuthReq
            {
                SessionId = _sessionId,
                PeerId = PeerId,
                CryptoPublicKey = _diffieHellman.PublicKey,
                CryptoKeySize = _diffieHellman.KeySize,
            });
        }
        
        private void SendCloseHandshake()
        {
            Send(new EngineProtocolDataC2S.NotifyClose());
        }
        
        private void SendMessageAck(long sequenceNumber)
        {
            Send(new EngineProtocolDataC2S.NotifyMessageAckInfo { SequenceNumber = sequenceNumber });
        }

        public void Close()
        {
            if (Interlocked.CompareExchange(ref _stateCode, NetPeerStateConst.Closed, NetPeerStateConst.None)
                == NetPeerStateConst.None)
            {
                // 초기 상태에서 종료를 호출함
                OnClosed();
                return;
            }

            if (Interlocked.CompareExchange(ref _stateCode, NetPeerStateConst.Closing, NetPeerStateConst.Connecting)
                == NetPeerStateConst.Connecting)
            {
                var session = _session;
                if (session != null && session.IsConnected)
                {
                    // 세션이 연결되어 있으면 종료함
                    session.Close();
                    return;
                }

                OnClosed();
                return;
            }

            _stateCode = NetPeerStateConst.Closing;

            // 종료 요청
            SendCloseHandshake();
            
            _timer.SetTimer(_ =>
            {
                if (_stateCode == NetPeerStateConst.Closed) 
                    return;
            
                // 종료 요청에 대한 응답을 받지 못했으면 즉시 종료함
                _session?.Close();
            }, null, 5000, Timeout.Infinite);
        }

        private void DequeueReceivedMessage()
        {
            // 연결된 상태에서만 메시지를 처리함
            if (State != ENetPeerState.Open)
                return;
            
            while (_receivedMessageQueue.TryDequeue(out var message))
            {
                // 순서대로 처리하기위해 대기 중인 메시지들을 확인함
                var messages = GetPendingReceivedMessages(message);
                foreach (var m in messages)
                    OnMessageReceived(m);
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
            
            var messages = CheckMessageTimeout(out var isLimitExceededReSend);
            if (isLimitExceededReSend)
            {
                // 재 전송 횟수 초과로 재 연결 처리함
                OnError(new Exception("The message resend limit has been exceeded."));
                OnClosed();
                return;
            }

            foreach (var message in messages)
                EnqueueSendingMessage(message);
        }
        
        public void Update()
        {
            _timer.Update();
            
            // 수신된 메시지 처리
            DequeueReceivedMessage();

            // 전송 메시지 처리
            DequeueSendingMessage();

            // 재 전송 메시지 체크
            CheckReSendMessages();
        }

        private ServerSession CreateNewSession()
        {
            // 세션 생성 및 이벤트 연결
            var session = new ServerSession();
            session.Opened += OnSessionOpened;
            session.Closed += OnSessionClosed;
            session.Error += OnSessionError;
            session.DataReceived += OnSessionDataReceived;
            return session;
        }

        private void OnSessionDataReceived(object sender, DataEventArgs e)
        {
            try
            {
                // 수신된 데이터 버퍼에 추가
                _messageFilter.AddBuffer(e.Data, e.Offset, e.Length);

                while (true)
                {
                    // 메시지 필터링
                    var message = _messageFilter.Filter(out var left);
                    if (message != null)
                    {
                        try
                        {
                            ExecuteMessage(message);
                        }
                        catch (Exception ex)
                        {
                            OnError(ex);
                        }
                    }

                    if (left <= 0)
                        break;
                }
            }
            catch (Exception ex)
            {
                OnError(ex);
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
            // 인증 요청
            SendAuthHandshake();
        }
        
        private void ExecuteMessage(IMessage message)
        {
            if (null == message)
                return;
            
            if (message.ProtocolId.IsEngineProtocol())
            {
                // 시스템 프로토콜
                var invoker = ProtocolManager.GetProtocolInvoker(message.ProtocolId);
                if (null == invoker)
                    throw new Exception($"The protocol invoker not found. protocolId={message.ProtocolId}");
                
                invoker.Invoke(this, message, null);
            }
            else
            {
                // 메시지 응답
                SendMessageAck(message.SequenceNumber);
                // 수신 메시지 큐잉
                EnqueueReceivedMessage(message);
            }   
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

        private void OnError(ESystemErrorCode errorCode)
        {
            OnError(new Exception($"The system error occurred: {errorCode}"));
        }

        private void OnClosed()
        {
            switch (_stateCode)
            {
                case NetPeerStateConst.Open:
                    // 오프라인으로 전환
                    OnOffline();
                    return;
                case NetPeerStateConst.Connecting:
                    return;
                default:
                    _stateCode = NetPeerStateConst.Closed;
                    _timer.Dispose();
                    _session = null;
                    Disconnected?.Invoke(this, EventArgs.Empty);
                    break;
            }
        }

        private void OnOpened()
        {
            _stateCode = NetPeerStateConst.Open;
            _connectionAttempts = 0;
            
            // 핑 타이머 시작
            StartPingTimer();
            
            // 대기 중인 메시지 전송
            var messages = GetPendingMessages();
            foreach (var message in messages)
                EnqueueSendingMessage(message);
            
            Connected?.Invoke(this, EventArgs.Empty);
        }

        private void OnOffline()
        {            
            // 전송 메시지 상태 초기화
            ResetSendingMessageStates();
            // 재 연결 시작
            StartConnection();
            Offline?.Invoke(this, EventArgs.Empty);
        }
        
        private void OnMessageReceived(IMessage message)
        {
            MessageReceived?.Invoke(this, new MessageEventArgs(message));
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

                _timer.Dispose();
                _latencySampler.Clear();
                ResetMessageProcessor();
            }

            _disposed = true;
        }

        [ProtocolHandler(EngineProtocolIdS2C.AuthAck)]
        private void OnAuthAck(EngineProtocolDataS2C.AuthAck authAck)
        {
            if (authAck.ErrorCode != ESystemErrorCode.Success)
            {
                // 인증 실패로 종료 함
                OnError(authAck.ErrorCode);
                Close();
                return;
            }
            
            PeerId = authAck.PeerId;
            _sessionId = authAck.SessionId;
            
            // 전송 타임아웃 시간 설정
            if (0 < authAck.SendTimeOutMs)
                SendTimeOutMs = authAck.SendTimeOutMs;
            
            // 최대 재 전송 횟수 설정
            if (0 < authAck.MaxReSendCnt)
                MaxReSendCnt = authAck.MaxReSendCnt;

            if (null != authAck.CryptoPublicKey)
            {
                // 암호화 키 생성
                _diffieHellman.DeriveSharedKey(authAck.CryptoPublicKey);
            }
            
            OnOpened();
        }

        [ProtocolHandler(EngineProtocolIdS2C.NotifyPongInfo)]
        private void OnNotifyPongInfo(EngineProtocolDataS2C.NotifyPongInfo info)
        {
            // 네트워크 레이턴시 계산
            var latencyMs = (int)Math.Round(DateTime.UtcNow.Subtract(info.SentTime).TotalMilliseconds, MidpointRounding.AwayFromZero);
            _latencySampler.Add(latencyMs);
            // 네트워크 레이턴시를 고려해 서버 시간 설정
            _lastServerTime = info.ServerTime.AddMilliseconds(latencyMs / 2.0f);
            _serverUpdateTime = DateTime.UtcNow;
            Console.WriteLine("latencyMs={0}, avgMs={1}, stddevMs={2}, serverTime={3:yyyy-MM-dd hh:mm:ss.fff}", latencyMs, AvgLatencyMs, LatencyStddevMs, _lastServerTime);
        }

        [ProtocolHandler(EngineProtocolIdS2C.NotifyMessageAckInfo)]
        private void OnNotifyMessageAckInfo(EngineProtocolDataS2C.NotifyMessageAckInfo info)
        {
            // 전송 한 메시지에 대한 시퀀스 번호 수신
            ReceiveMessageAck(info.SequenceNumber);
        }

        [ProtocolHandler(EngineProtocolIdS2C.NotifyClose)]
        private void OnNotifyClose(EngineProtocolDataS2C.NotifyClose notifyClose)
        {
            //OnDebug("Received a termination request from the server. state={0}", State);
            if (_stateCode == NetPeerStateConst.Closing)
            {
                // 클라이언트 요청으로 받은 경우, 즉시 종료함
                _session?.Close();
                return;
            }

            // 서버 요청으로 종료함
            Close();
        }
    }
}
