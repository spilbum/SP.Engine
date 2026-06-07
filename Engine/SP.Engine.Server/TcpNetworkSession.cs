using System;
using System.Net.Sockets;
using System.Threading;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Server;

public class TcpNetworkSession(Socket client, SocketSendContext sendContext, SocketReceiveContext receiveContext)
    : NetworkSessionBase(SocketMode.Tcp, client), IReliableSender
{
    private SocketSendContext _sendContext = sendContext;
    private SocketReceiveContext _receiveContext = receiveContext;
    private int _isSending; // 0: Idle, 1: Sending
    
    public SocketSendContext ReleaseSendContext()
    {
        return Interlocked.Exchange(ref _sendContext, null);
    }

    public SocketReceiveContext ReleaseReceiveContext()
    {
        return Interlocked.Exchange(ref _receiveContext, null);
    }

    public bool Start()
    {
        if (Session == null) return false;

        _sendContext.Initialize(this);
        _receiveContext.Initialize(this);
        StartReceive(_receiveContext.SocketEventArgs);
        return true;
    }
    
    private void StartReceive(SocketAsyncEventArgs e)
    {
        if (IsClosed) return;
        
        if (!IncrementIo()) return;
        
        if (!TryAddState(SocketState.InReceiving))
        {
            DecrementIo();
            return;
        }
        
        var offset = _receiveContext.OriginOffset;
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
        OnReceiveEnded();
        Close(reason);
    }
    
    public bool TrySend(TcpMessage message)
    {
        try
        {
            if (IsClosed || IsInClosingOrClosed) return false;

            if (!message.TryGetBuffer(out var memory))
            {
                Session.Logger.Warn("Session {0} TryGetBuffer failed. Message already disposed or invalid",
                    Session.SessionId);
                return false;
            }
            
            var context = Volatile.Read(ref _sendContext);
            if (context == null)
                return false;
            
            if (!context.RingBuffer.TryWrite(memory.Span))
            {
                Session.Logger.Warn("Session {0} TryWrite failed. RingBuffer: {1}/{2}", 
                    Session.SessionId, context.RingBuffer.Size, context.RingBuffer.Capacity);
                return false;
            }
        }
        catch (Exception ex)
        {
            LogError(ex);
            return false;
        }

        TryFlushSend();
        return true;
    }

    private void TryFlushSend()
    {
        if (Interlocked.CompareExchange(ref _isSending, 1, 0) == 0)
        {
            Session.AsyncRun(StartSend);
        }
    }

    private void StartSend()
    {
        while (true)
        {
            if (IsClosed)
            {
                Interlocked.Exchange(ref _isSending, 0);
                return;
            }

            var context = Volatile.Read(ref _sendContext);
            if (context == null)
            {
                Interlocked.Exchange(ref _isSending, 0);
                return;
            }
            
            var segment = context.RingBuffer.GetReadableSegment();
            if (segment.Count == 0)
            {
                Interlocked.Exchange(ref _isSending, 0);
                return;
            }

            if (!IncrementIo())
            {
                Interlocked.Exchange(ref _isSending, 0);
                return;
            }

            if (!TryAddState(SocketState.InSending))
            {
                DecrementIo();
                Interlocked.Exchange(ref _isSending, 0);
                return;
            }
        
            context.SocketEventArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);

            try
            {
                if (!_client.SendAsync(context.SocketEventArgs))
                {
                    if (HandleSendResult(context.SocketEventArgs))
                        continue;
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
                RemoveState(SocketState.InSending);
                DecrementIo();
                Close(CloseReason.SocketError);
            }

            break;
        }
    }

    private bool HandleSendResult(SocketAsyncEventArgs e)
    {
        RemoveState(SocketState.InSending);
        DecrementIo();

        if (e.SocketError != SocketError.Success || e.BytesTransferred == 0)
        {
            Close(e.BytesTransferred == 0 ? CloseReason.ClientClosing : CloseReason.SocketError);
            return false;
        }
        
        var context = Volatile.Read(ref _sendContext);
        context?.RingBuffer.AdvanceRead(e.BytesTransferred);

        return true;
    }

    public void ProcessSend(SocketAsyncEventArgs e)
    {
        if (HandleSendResult(e))
        {
            StartSend();
        }
    }
}
