using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using SP.Core.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;
using SP.Engine.Server.Logging;

namespace SP.Engine.Server;

public interface ITcpNetworkSession : IReliableSender, ILogContext
{
    SocketReceiveContext ReceiveContext { get; }
}

public class TcpNetworkSession : BaseNetworkSession, ITcpNetworkSession
{
    private SegmentQueue _sendingQueue;
    private readonly IObjectPool<SegmentQueue> _sendingQueuePool;
    private SocketAsyncEventArgs _sendEventArgs;
    private volatile int _inSendingFlag;
    private readonly SessionSendBuffer _sendBuffer = new(1024 * 64);
    private Timer _backPressureTimeoutTimer;
    private readonly object _backPressureLock = new();
    
    public SocketReceiveContext ReceiveContext { get; }
    public ILogger Logger => LogManager.GetLogger<TcpNetworkSession>();
    
    public TcpNetworkSession(Socket client, SocketReceiveContext context, IObjectPool<SegmentQueue> sendingQueuePool) 
        : base (SocketMode.Tcp, client)
    {
        ReceiveContext = context;
        ReceiveContext.Initialize(this);
        
        _sendingQueuePool = sendingQueuePool;
        if (_sendingQueuePool.TryRent(out _sendingQueue))
            _sendingQueue.StartEnqueue();
        else
        {
            throw new InvalidOperationException("Failed to rent segment queue.");
        }
        
        _sendEventArgs = new SocketAsyncEventArgs();
        _sendEventArgs.Completed += OnSendCompleted;
    }
    
    public void StartReceive()
    {
        if (!IncrementIo()) return;

        try
        {
            if (!TryAddState(SocketState.InReceiving))
            {
                DecrementIo();
                return;
            }
            
            var e = ReceiveContext.SocketEventArgs;
            if (!_client.ReceiveAsync(e))
                ProcessReceive(e);
        }
        catch (Exception e)
        {
            HandleNetworkError(e);
            OnReceiveEnded();
        }
    }
    
    public void ProcessReceive(SocketAsyncEventArgs e)
    {
        try
        {
            if (e.SocketError != SocketError.Success || e.BytesTransferred == 0)
            {
                OnReceiveTerminated(e.BytesTransferred == 0 ? CloseReason.ClientClosing : CloseReason.SocketError);
                return;
            }
            
            Session.ProcessTcpBuffer(e.Buffer, e.Offset, e.BytesTransferred);

            if (!IsPaused && !IsInClosingOrClosed)
            {
                if (!_client.ReceiveAsync(e))
                    ProcessReceive(e);

                return;
            }
            
            OnReceiveEnded();
        }
        catch (Exception ex)
        {
            LogError(ex);
            OnReceiveEnded();
            Close(CloseReason.ProtocolError);
        }
    }

    private void OnReceiveEnded()
    {
        RemoveState(SocketState.InReceiving);
        DecrementIo();
    }
    
    private void OnReceiveTerminated(CloseReason reason)
    {
        OnReceiveEnded();
        Close(reason);
    }
    
    public bool TrySend(TcpMessage message)
    {
        if (IsClosed) return false;

        if (!_sendBuffer.TryReserve(message.Size, out var segment, out var span))
            return false;

        message.WriteTo(span);
        
        Send(segment);
        return true;
    }
    
    private void Send(ArraySegment<byte> segment)
    {
        if (IsClosed || !_sendingQueue.Enqueue(segment, _sendingQueue.TrackId)) return;
        if (Interlocked.CompareExchange(ref _inSendingFlag, 1, 0) == 0)
            ExecuteSend(false);
    }
    
    private void ExecuteSend(bool isContinuation)
    {
        if (!isContinuation)
        {
            if (!IncrementIo()) return;
            if (!TryAddState(SocketState.InSending))
            {
                Interlocked.Exchange(ref _inSendingFlag, 0);
                DecrementIo();
                return;
            }
        }

        var client = _client;
        if (client == null || IsClosed)
        {
            Interlocked.Exchange(ref _inSendingFlag, 0);
            OnSendEnded();
            return;
        }
        
        try
        {
            HandleArgsCleanup(_sendEventArgs);

            var count = _sendingQueue.Count;
            switch (count)
            {
                case 0:
                    Interlocked.Exchange(ref _inSendingFlag, 0);
                    return;
                case 1:
                {
                    var segment = _sendingQueue[0];
                    _sendEventArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);
                    break;
                }
                default:
                    _sendEventArgs.BufferList = _sendingQueue;
                    break;
            }

            if (!_client.SendAsync(_sendEventArgs))
                ProcessSend(_sendEventArgs);
        }
        catch (Exception ex)
        {
            HandleNetworkError(ex);
            OnSendEnded();
        }
    }
    
    private void ProcessSend(SocketAsyncEventArgs e)
    {
        try
        {
            if (e.SocketError != SocketError.Success)
            {
                OnSendEnded();
                Close(CloseReason.SocketError);
                return;
            }

            if (e.BytesTransferred > 0)
                _sendBuffer.Release(e.BytesTransferred);
        
            var totalRequested = _sendingQueue.Sum(segment => segment.Count);
            if (e.BytesTransferred < totalRequested)
            {
                // Session.Logger.Debug("[TCP] Partial send occurred: {0}/{1} bytes.",
                //     e.BytesTransferred, totalRequested);

                // 전송된 만큼 큐에서 잘라내고 재전송
                _sendingQueue.TrimSentBytes(e.BytesTransferred);
                ExecuteSend(true);
                return;
            }
        
            HandleArgsCleanup(e);
            _sendingQueue.Clear();

            if (_sendingQueue.Count > 0)
            {
                ExecuteSend(true);
            }
            else
            {
                Interlocked.Exchange(ref _inSendingFlag, 0);
                if (_sendingQueue.Count > 0 && Interlocked.CompareExchange(ref _inSendingFlag, 1, 0) == 0)
                {
                    ExecuteSend(true);
                }
                else
                {
                    OnSendEnded();   
                }
            }
        }
        catch (Exception ex)
        {
            LogError(ex);
            OnSendEnded();
        }
    }
    
    public void PauseReceive()
    {
        if (IsPaused) return;
        if (!TryAddState(SocketState.Paused)) return;
        
        lock (_backPressureLock)
        {
            _backPressureTimeoutTimer?.Dispose();
            _backPressureTimeoutTimer = new Timer(_ =>
            {
                Logger.Debug("BackPressure timeout reached. Force closing session: {0}", Session.SessionId);
                Close(CloseReason.ServerBusy);
            }, null, 10000, Timeout.Infinite);
        }
    }

    public void ResumeReceive()
    {
        if (!IsPaused) return;
        if (!RemoveState(SocketState.Paused)) return;

        lock (_backPressureLock)
        {
            _backPressureTimeoutTimer?.Dispose();
            _backPressureTimeoutTimer = null;
        }

        StartReceive();
    }
    
    protected override void OnRelease()
    {
        lock (_backPressureLock)
        {
            _backPressureTimeoutTimer?.Dispose();
            _backPressureTimeoutTimer = null;   
        }
        
        var e = Interlocked.Exchange(ref _sendEventArgs, null);
        if (e != null)
        {
            e.Completed -= OnSendCompleted;
            e.Dispose();
        }

        var queue = Interlocked.Exchange(ref _sendingQueue, null);
        if (queue != null)
        {
            queue.Clear();
            _sendingQueuePool.Return(queue);
        }

        _sendBuffer.Dispose();
    }

    private void OnSendEnded()
    {
        RemoveState(SocketState.InSending);
        DecrementIo();
    }

    private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
    {
        ProcessSend(e);
    }

    private static void HandleArgsCleanup(SocketAsyncEventArgs e)
    {
        e.UserToken = null;
        e.BufferList = null;
        e.SetBuffer(null, 0, 0);
    }
}
