using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SP.Core;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Server;

public class TcpNetworkSession : NetworkSessionBase, IReliableSender
{
    private const int MessageBufferLength = 4096;
    
    private readonly Channel<(PooledBuffer Buffer, int Length)> _sendChannel;
    private readonly CancellationTokenSource _cts = new();
    private Timer _resumeTimeoutTimer;
    
    public SocketReceiveContext ReceiveContext { get; }

    public TcpNetworkSession(Socket client, SocketReceiveContext context) 
        : base (SocketMode.Tcp, client)
    {
        ReceiveContext = context;
        ReceiveContext.Initialize(this);

        var channelOptions = new BoundedChannelOptions(2048)
        {
            SingleReader = true,  // ProcessSendLoopAsync 에서 Read 처리
            SingleWriter = false, // 멀티스레드에서 Write 처리
            FullMode = BoundedChannelFullMode.DropWrite // 버퍼가 꽉 차면 쓰기를 실패 처리하여 백업 제어
        };
        
        _sendChannel = Channel.CreateBounded<(PooledBuffer, int)>(channelOptions);
        _resumeTimeoutTimer = new Timer(OnResumeTimeout, null, Timeout.Infinite, Timeout.Infinite);
        Task.Run(ProcessSendLoopAsync);
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
        if (IsClosed || IsInClosingOrClosed) return false;

        var messageSize = message.Size;
        var bufferCapacity = messageSize <= MessageBufferLength ? MessageBufferLength : messageSize;
        var buffer = new PooledBuffer(bufferCapacity);

        try
        {
            message.WriteTo(buffer[..messageSize]);

            if (!_sendChannel.Writer.TryWrite((buffer, messageSize)))
            {
                buffer.Dispose();
                return false;
            }
        }
        catch (Exception ex)
        {
            LogError(ex);
            buffer.Dispose();
            return false;
        }

        return true;
    }

    private async Task ProcessSendLoopAsync()
    {
        var reader = _sendChannel.Reader;
        var token = _cts.Token;

        try
        {
            while (await reader.WaitToReadAsync(token))
            {
                while (reader.TryRead(out var item))
                {
                    var buffer = item.Buffer;
                    var length = item.Length;
                    
                    if (IsClosed || _client == null)
                    {
                        buffer.Dispose();
                        return;
                    }

                    if (!IncrementIo())
                    {
                        buffer.Dispose();
                        return;
                    }

                    if (!TryAddState(SocketState.InSending))
                    {
                        DecrementIo();
                        buffer.Dispose();
                        return;
                    }

                    var isSocketError = false;
                    try
                    {
                        await _client.SendAsync(buffer.Memory[..length], SocketFlags.None, token);
                    }
                    catch (Exception ex)
                    {
                        LogError(ex);
                        isSocketError = true;
                    }
                    finally
                    {
                        buffer.Dispose();
                        RemoveState(SocketState.InSending);
                        DecrementIo();
                    }

                    if (isSocketError)
                    {
                        Close(CloseReason.SocketError);
                        return;
                    }
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            LogError(ex);
        }
    }
    
    public void PauseReceive()
    {
        if (IsPaused) return;
        if (!TryAddState(SocketState.Paused)) return;
        
        Session.Logger.Debug("Session {0} is Paused", Session.SessionId);
        _resumeTimeoutTimer.Change(10000, Timeout.Infinite);
    }

    public void ResumeReceive()
    {
        if (!IsPaused) return;
        if (!RemoveState(SocketState.Paused)) return;

        Session.Logger.Debug("Session {0} is Resume.", Session.SessionId);
        
        _resumeTimeoutTimer.Change(Timeout.Infinite, Timeout.Infinite);
        if (!HasState(SocketState.InReceiving))
        {
            Start();
        }
    }

    private void OnResumeTimeout(object state)
    {
        Session.Logger.Warn("Resume pending timeout reached. Force closing session due to back-pressure: {0}",
            Session.SessionId);
        Close(CloseReason.ServerBusy);
    }
    
    protected override void OnRelease()
    {
        _resumeTimeoutTimer.Dispose();
        
        _sendChannel.Writer.Complete();
        _cts.Cancel();

        while (_sendChannel.Reader.TryRead(out var item)) item.Buffer.Dispose();
        _cts.Dispose();
    }
}
