using System;
using System.Linq;
using System.Net.Sockets;
using System.Threading;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Server;

public class TcpNetworkSession : NetworkSessionBase, IReliableSender
{
    private readonly SessionSendBuffer _sendBuffer;
    private SocketAsyncEventArgs _sendEventArgs;
    private readonly IObjectPool<SegmentQueue> _sendingQueuePool;
    private SegmentQueue _sendingQueue;
    private Timer _resumeTimeoutTimer;
    
    public SocketReceiveContext ReceiveContext { get; }

    public TcpNetworkSession(
        Socket client, 
        SocketReceiveContext context, 
        IObjectPool<SegmentQueue> sendingQueuePool) 
        : base (SocketMode.Tcp, client)
    {
        ReceiveContext = context;
        ReceiveContext.Initialize(this);
        
        _sendingQueuePool = sendingQueuePool;
        
        _sendEventArgs = new SocketAsyncEventArgs();
        _sendEventArgs.Completed += OnSendCompleted;
        _sendBuffer = new SessionSendBuffer(1024 * 4);
    }

    public void Start()
    {
        if (!_sendingQueuePool.TryRent(out var queue) || queue == null)
        {
            Close(CloseReason.InternalError);
            return;
        }
        
        _sendingQueue = queue;
        _sendingQueue.StartEnqueue();
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
            {
                ProcessReceive(e);
            }
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
        Session.Logger.Debug("Session {0} receive terminated. Reason: {1}", Session.SessionId, reason);
        OnReceiveEnded();
        Close(reason);
    }
    
    public bool TrySend(TcpMessage message)
    {
        if (IsClosed) return false;

        if (!_sendBuffer.TryReserve(message.Size, out var segment)) return false;
        message.WriteTo(segment);

        while (true)
        {
            var queue = _sendingQueue;
            if (queue == null || queue.IsFull) return false;

            if (queue.Enqueue(segment, queue.TrackId))
            {
                ExecuteSend(queue, queue.TrackId, false);
                break;
            }
            
            if (IsClosed) return false;
        }
        
        return true;
    }
    
    private void ExecuteSend(SegmentQueue queue, int trackId, bool isContinuation)
    {
        if (queue == null) return;
        
        if (!isContinuation)
        {
            if (!IncrementIo()) return;
            if (!TryAddState(SocketState.InSending))
            {
                DecrementIo();
                return;
            }

            var currQueue = _sendingQueue;
            if (queue != currQueue || trackId != currQueue.TrackId)
            {
                HandleSendEnd();
                return;
            }
        }

        if (IsInClosingOrClosed)
        {
            HandleSendEnd();
            return;
        }

        if (!_sendingQueuePool.TryRent(out var newQueue))
        {
            HandleSendError(queue);
            Close(CloseReason.InternalError);
            return;
        }
        
        var oldQueue = Interlocked.CompareExchange(ref _sendingQueue, newQueue, queue);
        if (!ReferenceEquals(oldQueue, queue))
        {
            if (newQueue != null) _sendingQueuePool.Return(newQueue);
            HandleSendEnd();
            return;
        }

        queue.StopEnqueue();
        newQueue.StartEnqueue();

        if (queue.Count == 0)
        {
            HandleSendEnd();
            Close(CloseReason.InternalError);
            return;
        }
        
        Send(queue);
    }

    private void Send(SegmentQueue queue)
    {
        var e = _sendEventArgs;
        
        try
        {
            e.UserToken = queue;
            
            if (queue.Count > 1)
            {
                e.BufferList = queue;
            }
            else
            {
                var segment = queue[0];
                e.SetBuffer(segment.Array, segment.Offset, segment.Count); 
            }

            var client = _client;
            if (client == null)
            {
                HandleSendError(queue);
                return;
            }

            if (!_client.SendAsync(e))
            {
                OnSendCompleted(client, e);
            }
        }
        catch (Exception ex)
        {
            LogError(ex);
            HandleArgsCleanup(e);
            HandleSendError(queue);
        }
    }

    private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
    {
        if (e.UserToken is not SegmentQueue queue)
            return;
        
        if (e.SocketError != SocketError.Success)
        {
            HandleArgsCleanup(e);
            HandleSendError(queue);
            return;
        }
        
        if (e.BytesTransferred > 0)
            _sendBuffer.Release(e.BytesTransferred);

        var count = queue.Sum(segment => segment.Count);
        if (count > e.BytesTransferred)
        {
            queue.TrimSentBytes(e.BytesTransferred);
            HandleArgsCleanup(e);
            Send(queue);
            
            Session.Logger.Debug("[TCP] Partial send occurred: {0}/{1} bytes.", 
                e.BytesTransferred, count);
            
            return;
        }
        
        HandleArgsCleanup(e);
        HandleSendCompleted(queue);
    }

    private void HandleSendCompleted(SegmentQueue queue)
    {
        queue.Clear();
        _sendingQueuePool.Return(queue);
        
        var newQueue = _sendingQueue;
        if (IsInClosingOrClosed)
        {
            if (newQueue is { Count: > 0 } && _client != null)
            {
                ExecuteSend(newQueue, newQueue.TrackId, true);
                return;
            }
            
            HandleSendEnd();
            return;
        }

        if (newQueue == null || newQueue.Count == 0)
        {
            HandleSendEnd();
        }
        else
        {
            ExecuteSend(newQueue, newQueue.TrackId, true);
        }
    }

    private void HandleSendEnd()
    {
        if (RemoveState(SocketState.InSending))
        {
            DecrementIo();
        }
    }

    private void HandleSendError(SegmentQueue queue)
    {
        queue.Clear();
        _sendingQueuePool.Return(queue);
        HandleSendEnd();
    }

    public void PauseReceive()
    {
        if (IsPaused) return;
        if (!TryAddState(SocketState.Paused)) return;
        
        Session.Logger.Debug("Session {0} is Paused", Session.SessionId);
        
        _resumeTimeoutTimer?.Dispose();
        _resumeTimeoutTimer = new Timer(_ =>
        {
            Session.Logger.Warn("Resume pending timeout reached. Force closing session due to back-pressure: {0}",
                Session.SessionId);
                
            Close(CloseReason.ServerBusy);
        }, null, 10000, Timeout.Infinite);
    }

    public void ResumeReceive()
    {
        if (!IsPaused) return;
        if (!RemoveState(SocketState.Paused)) return;

        Session.Logger.Debug("Session {0} is Resume.", Session.SessionId);
        
        if (_resumeTimeoutTimer != null)
        {
            _resumeTimeoutTimer.Dispose();
            _resumeTimeoutTimer = null;   
        }

        if (!HasState(SocketState.InReceiving))
        {
            Start();
        }
    }
    
    protected override void OnRelease()
    {
        if (_resumeTimeoutTimer != null)
        {
            _resumeTimeoutTimer.Dispose();
            _resumeTimeoutTimer = null;   
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
