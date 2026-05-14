using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using SP.Core.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Server;

public interface ITcpNetworkSession : IReliableSender, ILogContext
{
    SocketReceiveContext ReceiveContext { get; }
}

public class TcpNetworkSession : NetworkSessionBase, ITcpNetworkSession
{
    private SegmentQueue _sendingQueue;
    private readonly IObjectPool<SegmentQueue> _sendingQueuePool;
    private SocketAsyncEventArgs _sendEventArgs;
    private volatile int _inSendingFlag;
    private readonly SessionSendBuffer _sendBuffer;
    private Timer _resumePendingTimeoutTimer;
    private readonly object _timerLock = new();
    
    public SocketReceiveContext ReceiveContext { get; }
    
    public TcpNetworkSession(Socket client, SocketReceiveContext context, IObjectPool<SegmentQueue> sendingQueuePool) 
        : base (SocketMode.Tcp, client)
    {
        ReceiveContext = context;
        ReceiveContext.Initialize(this);
        
        _sendingQueuePool = sendingQueuePool;
        if (!_sendingQueuePool.TryRent(out _sendingQueue))
        {
            throw new InvalidOperationException("Failed to rent segment queue.");
        }
        
        _sendingQueue.StartEnqueue();
        
        _sendEventArgs = new SocketAsyncEventArgs();
        _sendEventArgs.Completed += OnSendCompleted;
        _sendBuffer = new SessionSendBuffer(1024 * 4);
    }

    public void Start()
    {
        StartReceive(ReceiveContext.SocketEventArgs);
    }
    
    private void StartReceive(SocketAsyncEventArgs e)
    {
        if (IsPaused || IsClosed) return;
        
        if (!IncrementIo()) return;
        
        if (!TryAddState(SocketState.InReceiving))
        {
            DecrementIo();
            return;
        }
        
        var offset = ReceiveContext.OriginOffset;
        if (e.Offset != offset)
            e.SetBuffer(offset, Session.Config.Network.ReceiveBufferSize);

        try
        {
            if (!_client.ReceiveAsync(e))
                ProcessReceive(e);
        }
        catch (Exception ex)
        {
            LogError(ex);
            OnReceiveTerminated(CloseReason.SocketError);
        }
    }
    
    public void ProcessReceive(SocketAsyncEventArgs e)
    {
        if (e.SocketError != SocketError.Success || e.BytesTransferred == 0)
        {
            OnReceiveTerminated(e.BytesTransferred == 0 ? CloseReason.ClientClosing : CloseReason.SocketError);
            return;
        }
        
        OnReceiveEnded();
        
        try
        {
            Session.ProcessTcpBuffer(e.Buffer, e.Offset, e.BytesTransferred);
        }
        catch (Exception ex)
        {
            LogError(ex);
            Close(CloseReason.InternalError);
            return;
        }
        
        StartReceive(e);
    }
    
    private void OnReceiveEnded()
    {
        RemoveState(SocketState.InReceiving);
        DecrementIo();
    }
    
    private void OnReceiveTerminated(CloseReason reason)
    {
        Logger.Debug("Session {0} receive terminated. Reason: {1}", Session.SessionId, reason);
        OnReceiveEnded();
        Close(reason);
    }
    
    public bool TrySend(TcpMessage message)
    {
        if (IsClosed) return false;

        if (!_sendBuffer.TryReserve(message.Size, out var segment)) 
            return false;
        
        message.WriteTo(segment.AsSpan());
        
        if (!_sendingQueue.Enqueue(segment, _sendingQueue.TrackId)) 
            return false;

        if (Interlocked.CompareExchange(ref _inSendingFlag, 1, 0) == 0)
            ExecuteSend(false);
        
        return true;
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

        while (true)
        {
            var client = _client;
            if (client == null || IsClosed)
            {
                FinalizeSend();
                return;
            }
        
            try
            {
                var e = _sendEventArgs;
                HandleArgsCleanup(e);

                if (_sendingQueue.Count == 0)
                {
                    Interlocked.Exchange(ref _inSendingFlag, 0);
                    if (_sendingQueue.Count > 0 && Interlocked.CompareExchange(ref _inSendingFlag, 1, 0) == 0)
                        continue;

                    FinalizeSend();
                    return;
                }

                var segment = _sendingQueue[0];
                if (_sendingQueue.Count == 1)
                    e.SetBuffer(segment.Array, segment.Offset, segment.Count);
                else
                    e.BufferList = _sendingQueue;

                if (_client.SendAsync(e))
                    return;

                if (!HandleSendResult(e))
                    return;
            }
            catch (Exception ex)
            {
                HandleNetworkError(ex);
                FinalizeSend();
                return;
            }
        }
    }

    private bool HandleSendResult(SocketAsyncEventArgs e)
    {
        if (e.SocketError != SocketError.Success)
        {
            Close(CloseReason.SocketError);
            FinalizeSend();
            return false;
        }
        
        if (e.BytesTransferred > 0)
            _sendBuffer.Release(e.BytesTransferred);

        var totalRequested = e.BufferList != null
            ? _sendingQueue.Sum(s => s.Count)
            : e.Count;

        if (e.BytesTransferred < totalRequested)
        {
            Session.Logger.Debug("[TCP] Partial send occurred: {0}/{1} bytes.",
                e.BytesTransferred, totalRequested);
            
            _sendingQueue.TrimSentBytes(e.BytesTransferred);
        }
        else
        {
            _sendingQueue.Clear();
        }
        
        return true;
    }

    private void FinalizeSend()
    {
        if (!RemoveState(SocketState.InSending)) return;
        Interlocked.Exchange(ref _inSendingFlag, 0);
        DecrementIo();
    }

    private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
    {
        if (HandleSendResult(e))
        {
            ExecuteSend(isContinuation: true);
        }
    }

    public void PauseReceive()
    {
        if (IsPaused) return;
        if (!TryAddState(SocketState.Paused)) return;
        
        Logger.Debug("Session {0} is Paused", Session.SessionId);
        
        lock (_timerLock)
        {
            _resumePendingTimeoutTimer?.Dispose();
            _resumePendingTimeoutTimer = new Timer(_ =>
            {
                Logger.Warn("Resume pending timeout reached. Force closing session due to back-pressure: {0}",
                    Session.SessionId);
                
                Close(CloseReason.ServerBusy);
            }, null, 10000, Timeout.Infinite);
        }
    }

    public void ResumeReceive()
    {
        if (!IsPaused) return;
        if (!RemoveState(SocketState.Paused)) return;

        Logger.Debug("Session {0} is Resume.", Session.SessionId);
        
        lock (_timerLock)
        {
            _resumePendingTimeoutTimer?.Dispose();
            _resumePendingTimeoutTimer = null;
        }

        if (!HasState(SocketState.InReceiving))
        {
            Start();
        }
    }
    
    protected override void OnRelease()
    {
        lock (_timerLock)
        {
            _resumePendingTimeoutTimer?.Dispose();
            _resumePendingTimeoutTimer = null;   
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

    private static void HandleArgsCleanup(SocketAsyncEventArgs e)
    {
        e.UserToken = null;
        e.BufferList = null;
        e.SetBuffer(null, 0, 0);
    }
}
