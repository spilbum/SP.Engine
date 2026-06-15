using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Threading;
using SP.Core.Logging;
using SP.Engine.Runtime;

namespace SP.Engine.Server;

public enum SocketMode
{
    Tcp = 0,
    Udp = 1
}

public interface INetworkSession : ILogContext
{
    SessionBase Session { get; }
}

public abstract class NetworkSessionBase : INetworkSession
{
    private const int StateConnected = 0;
    private const int StateClosing = 1;
    private const int StateClosed = 2;
    
    private Action<INetworkSession, CloseReason> _closed;
    private int _state;
    private int _pendingIoCount;
    private protected IPEndPoint _remoteEndPoint;
    private CloseReason _finalReason;
    private readonly SocketMode _mode;
    private volatile SessionBase _session;
    protected Socket _client;

    public SessionBase Session
    {
        get => _session;
        internal set => Interlocked.Exchange(ref _session, value);
    }

    public ILogger Logger => _session?.Logger;
    
    public IPEndPoint LocalEndPoint { get; }
    public IPEndPoint RemoteEndPoint => Volatile.Read(ref _remoteEndPoint);
    
    public bool IsClosed => Volatile.Read(ref _state) == StateClosed; 
    protected bool IsInClosingOrClosed => Volatile.Read(ref _state) >= StateClosing;
    
    protected NetworkSessionBase(SocketMode mode, Socket client)
    {
        _mode = mode;
        _client = client;
        if (client != null)
        {
            LocalEndPoint = (IPEndPoint)client.LocalEndPoint;
            Volatile.Write(ref _remoteEndPoint, (IPEndPoint)client.RemoteEndPoint);
        }
        _state = StateConnected;
    }
    
    public event Action<INetworkSession, CloseReason> Closed
    {
        add => _closed += value;
        remove => _closed -= value;
    }

    protected bool IncrementIo()
    {
        if (IsInClosingOrClosed) return false;
        Interlocked.Increment(ref _pendingIoCount);
        return true;
    }

    protected void DecrementIo()
    {
        if (Interlocked.Decrement(ref _pendingIoCount) != 0) return;
        if (IsInClosingOrClosed)
        {
            OnClosed(_finalReason);
        }
    }
    
    public void Close(CloseReason reason)
    { 
        if (Interlocked.CompareExchange(ref _state, StateClosing, StateConnected) != StateConnected)
            return;
        
        _finalReason = reason;
        
        var client = Interlocked.Exchange(ref _client, null);
        if (client != null && ShouldSocketClosed())
        {
            client.SafeClose();
        }

        if (Volatile.Read(ref _pendingIoCount) == 0)
        {
            OnClosed(_finalReason);
        }
    }

    protected virtual void OnClosed(CloseReason reason)
    {
        if (Interlocked.Exchange(ref _state, StateClosed) == StateClosed)
            return;
        
        OnRelease();

        var handler = Interlocked.Exchange(ref _closed, null);
        handler?.Invoke(this, reason);
    }
    
    protected virtual void OnRelease() { }
    protected virtual bool ShouldSocketClosed() => true;

    protected void LogError(Exception e, [CallerMemberName] string caller = "", [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = -1)
    {
        if (ShouldIgnoreError(e)) return;

        var sessionId = Session.SessionId;
        var fileName = System.IO.Path.GetFileName(filePath);
        
        if (e is SocketException socketEx)
        {
            Session.Logger.Error(
                "[NetworkError] SessionId: {SessionId}, Mode: {Mode}\nMessage: {Message}\nSocketErrorCode: {ErrorCode} ({SocketError})\nLocation: {Caller} in {FileName}:{LineNumber}\nStackTrace:\n{StackTrace}",
                sessionId, _mode, e.Message, (int)socketEx.SocketErrorCode, socketEx.SocketErrorCode, caller, fileName, lineNumber, e.StackTrace);
        }
        else
        {
            Session.Logger.Error(
                "[NetworkError] SessionId: {SessionId}, Mode: {Mode}\nMessage: {Message}\nLocation: {Caller} in {FileName}:{LineNumber}\nStackTrace:\n{StackTrace}",
                sessionId, _mode, e.Message, caller, fileName, lineNumber, e.StackTrace);
        }
    }

    private bool ShouldIgnoreError(Exception e)
    {
        switch (e)
        {
            case SocketException se when IsIgnoreSocketError((int)se.SocketErrorCode):
            case ObjectDisposedException or InvalidOperationException:
            case OperationCanceledException:
                return true;
            default:
                return false;
        }
    }

    protected virtual bool IsIgnoreSocketError(int errorCode)
    {
        var error = (SocketError)errorCode;
        return error switch
        {
            SocketError.ConnectionReset => true,    // 10054: 상대방 강제 종료
            SocketError.ConnectionAborted => true,  // 10053: 연결 중단
            SocketError.TimedOut => true,           // 10060: 시간 초과
            SocketError.OperationAborted => true,   // 995: 비동기 작업 취소 (중요)
            SocketError.Shutdown => true,           // 10058: 종료된 소켓에 작업
            _ => false
        };
    }
}
