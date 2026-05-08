using System;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using SP.Core.Logging;
using SP.Engine.Runtime;

namespace SP.Engine.Server;

[Flags]
public enum SocketState
{
    None = 0,
    InSending = 1 << 0,
    InReceiving = 1 << 1,
    Paused = 1 << 2,
    InClosing = 1 << 4,
    Closed = 1 << 24
}

public enum SocketMode
{
    Tcp = 0,
    Udp = 1
}

public interface INetworkSession
{
    BaseSession Session { get; }
}

public abstract class BaseNetworkSession : INetworkSession, ILogContext
{
    private const string LogHeaderFormat = "[NetworkError] SessionId: {0}, Mode: {1}";
    private const string SocketInfoFormat = "SocketErrorCode: {0} ({1})";
    private const string CallerInfoFormat = "Location: {0} in {1}:{2}";

    protected Socket _client;
    private Action<INetworkSession, CloseReason> _closed;
    private volatile int _socketState;
    private int _pendingIoCount;
    private protected IPEndPoint _remoteEndPoint;
    private CloseReason _finalReason;
    
    protected BaseNetworkSession(SocketMode mode, Socket client)
    {
        _client = client;
        Mode = mode;
        LocalEndPoint = (IPEndPoint)client.LocalEndPoint;
        RemoteEndPoint = (IPEndPoint)client.RemoteEndPoint;
    }

    public BaseSession Session { get; internal set; }
    public SocketMode Mode { get; }
    public IPEndPoint LocalEndPoint { get; }
    public IPEndPoint RemoteEndPoint
    {
        get => Volatile.Read(ref _remoteEndPoint);
        protected set => Volatile.Write(ref _remoteEndPoint, value);
    }
    
    public bool IsClosed => HasState(SocketState.Closed);
    public bool IsPaused => HasState(SocketState.Paused);
    public bool IsIdle => (_socketState & ((int)SocketState.InSending | (int)SocketState.InReceiving)) == 0;
    public bool IsInClosingOrClosed => _socketState >= (int)SocketState.InClosing;
    
    public ILogger Logger => Session.Logger;
    
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
        if (Interlocked.Decrement(ref _pendingIoCount) == 0)
        {
            if (IsInClosingOrClosed)
            {
                OnClosed(_finalReason);
            }
        }
    }
    
    public void Close(CloseReason reason)
    {
        if (!TryAddState(SocketState.InClosing)) return;
        _finalReason = reason;
        
        if (_client != null)
            InternalClose(reason);
        else
        {
            if (Volatile.Read(ref _pendingIoCount) == 0)
                OnClosed(_finalReason);
        }
    }

    private void InternalClose(CloseReason reason)
    {
        var client = Interlocked.Exchange(ref _client, null);
        if (client == null) return;
        
        if (ShouldSocketClosed())
            client.SafeClose();
        
        if (Volatile.Read(ref _pendingIoCount) == 0)
            OnClosed(reason);
    }
    
    protected virtual void OnClosed(CloseReason reason)
    {
        if (!TryAddState(SocketState.Closed)) return;

        OnRelease();
        
        var handler = Interlocked.Exchange(ref _closed, null);
        handler?.Invoke(this, reason);
    }
    
    protected abstract void OnRelease();
    protected virtual bool ShouldSocketClosed() => true;
    
    protected bool HasState(SocketState state) => (_socketState & (int)state) != 0;

    protected bool TryAddState(SocketState state)
    {
        while (true)
        {
            var current = _socketState;
            if (((SocketState)current & state) != 0) return false;

            var next = current | (int)state;
            if (Interlocked.CompareExchange(ref _socketState, next, current) == current) 
                return true;
        }
    }

    protected bool RemoveState(SocketState state)
    {
        while (true)
        {
            var current = _socketState;
            var next = current & ~(int)state;
            if (Interlocked.CompareExchange(ref _socketState, next, current) == current)
                return true;
        }
    }

    protected void HandleNetworkError(Exception e)
    {
        LogError(e);
        Close(CloseReason.SocketError);
    }

    protected void LogError(Exception e, [CallerMemberName] string caller = "", [CallerFilePath] string filePath = "",
        [CallerLineNumber] int lineNumber = -1)
    {
        if (ShouldIgnoreError(e)) return;

        var sessionId = Session.SessionId;
        var logBuilder = new StringBuilder();

        logBuilder.AppendLine(string.Format(LogHeaderFormat, sessionId, Mode));
        logBuilder.AppendLine($"Message: {e.Message}");

        if (e is SocketException socketEx)
        {
            logBuilder.AppendLine(string.Format(SocketInfoFormat,
                (int)socketEx.SocketErrorCode,
                socketEx.SocketErrorCode));
        }
        
        logBuilder.AppendLine(string.Format(CallerInfoFormat, caller, System.IO.Path.GetFileName(filePath), lineNumber));
        logBuilder.AppendLine("StackTrace:");
        logBuilder.AppendLine(e.StackTrace);
        
        Logger.Error(logBuilder.ToString());
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
