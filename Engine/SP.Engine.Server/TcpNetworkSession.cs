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
    
    public SocketSendContext ReleaseSendContext() => Interlocked.Exchange(ref _sendContext, null);
    public SocketReceiveContext ReleaseReceiveContext() => Interlocked.Exchange(ref _receiveContext, null);

    public bool Start()
    {
        if (Session == null) return false;

        _sendContext.Initialize(this);
        _receiveContext.Initialize(this);
        StartReceive();
        return true;
    }
    
    private void StartReceive()
    {
        if (IsClosed) return;
        
        if (!IncrementIo()) return;

        var e = _receiveContext.SocketEventArgs;
        var offset = _receiveContext.OriginOffset;

        while (true)
        {
            if (e.Offset != offset)
                e.SetBuffer(offset, Session.Config.Network.ReceiveBufferSize);

            var client = Volatile.Read(ref _client);
            if (client == null)
                return;
            
            bool pending;
            try
            {
                pending = client.ReceiveAsync(e);
            }
            catch (Exception ex)
            {
                LogError(ex);
                Close(CloseReason.SocketError);
                break;
            }

            if (pending) return;

            if (!ProcessReceiveCompleted(e))
                break;
        }
        
        DecrementIo();
    }
    
    public void ProcessReceive(SocketAsyncEventArgs e)
    {
        if (ProcessReceiveCompleted(e))
        {
            StartReceive();
        }
        
        DecrementIo();
    }

    private bool ProcessReceiveCompleted(SocketAsyncEventArgs e)
    {
        if (e.SocketError != SocketError.Success || e.BytesTransferred == 0)
        {
            Close(e.BytesTransferred == 0 ? CloseReason.ClientClosing : CloseReason.SocketError);
            return false;
        }

        try
        {
            Session.ProcessTcpBuffer(e.Buffer, e.Offset, e.BytesTransferred);
            return true;
        }
        catch (Exception ex)
        {
            LogError(ex);
            Close(CloseReason.InternalError);
            return false;
        }
    }
    
    public bool TrySend(TcpMessage message)
    {
        try
        {
            if (IsInClosingOrClosed) return false;

            if (!message.TryGetBuffer(out var memory))
                return false;
            
            var context = Volatile.Read(ref _sendContext);
            if (context == null)
                return false;

            if (!context.RingBuffer.TryWrite(memory.Span))
                return false;
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
        var syncCount = 0;
        const int MaxSyncCompletions = 50;
        
        while (true)
        {
            if (IsInClosingOrClosed)
            {
                Interlocked.Exchange(ref _isSending, 0);
                return;
            }
            
            var client = Volatile.Read(ref _client);
            if (client == null)
                return;

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
        
            context.SocketEventArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);

            bool pending;
            try
            {
                pending = client.SendAsync(context.SocketEventArgs);
            }
            catch (Exception ex)
            {
                LogError(ex);
                Interlocked.Exchange(ref _isSending, 0);
                DecrementIo();
                Close(CloseReason.SocketError);
                return;
            }

            if (pending) break;

            if (!ProcessSendCompleted(context.SocketEventArgs))
            {
                Interlocked.Exchange(ref _isSending, 0);
                return;
            }
            
            syncCount++;
            if (syncCount >= MaxSyncCompletions)
            {
                Session.AsyncRun(StartSend);
                return;
            }
        }
    }

    private bool ProcessSendCompleted(SocketAsyncEventArgs e)
    {
        try
        {
            if (e.SocketError != SocketError.Success || e.BytesTransferred == 0)
            {
                Close(e.BytesTransferred == 0 ? CloseReason.ClientClosing : CloseReason.SocketError);
                return false;
            }

            var context = Volatile.Read(ref _sendContext);
            context?.RingBuffer.AdvanceRead(e.BytesTransferred);
            return true;
        }
        finally
        {
            DecrementIo();
        }
    }

    public void ProcessSend(SocketAsyncEventArgs e)
    {
        if (ProcessSendCompleted(e))
        {
            StartSend();
        }
        else
        {
            Interlocked.Exchange(ref _isSending, 0);
        }
    }
}
