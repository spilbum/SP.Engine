using Common.Protocol.CC2CS;
using SP.Engine.Client;

namespace ChatClient;

public class UserPeer : NetPeerBase
{
    private CancellationTokenSource? _cts;
    private int _pendingCount;
    private int _batchCount;

    
    public string UserId { get; }
    public string TargetUserId { get; }
    public bool IsLoggedIn { get; set; }
    public bool IsSendingChat { get; private set; }
    
    public UserPeer(string userId, string targetUserId)
    {
        UserId = userId;
        TargetUserId = targetUserId;
        Connected += OnConnected;
        Error += OnError;
    }
    
    private void OnConnected(object? sender, EventArgs e)
    {
        Logger.Debug("Peer {0} connected.", UserId);
        Send(new LoginReq { UserId = UserId });
    }

    private void OnError(object? sender, ErrorEventArgs e)
    {
        var ex = e.GetException();
        Logger.Error("An error occurred: {0}\nStacktrace: {1}", ex.Message, ex.StackTrace);  
    }

    public void StartChatTest(int period, int batchCount)
    {
        if (IsSendingChat) return;
        IsSendingChat = true;
        _batchCount = batchCount;
        _cts = new CancellationTokenSource();
        _ = Task.Run(() => ChatScheduler(period, _cts.Token));
    }

    public void StopChatTest()
    {
        if (!IsSendingChat) return;
        IsSendingChat = false;
        _cts?.Cancel();
    }

    private async Task ChatScheduler(int period, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            Interlocked.Add(ref _pendingCount, _batchCount);

            if (period > 0) await Task.Delay(period, ct);
            else await Task.Yield();
        }
    }

    public void ProcessPendingChat()
    {
        var count = Interlocked.Exchange(ref _pendingCount, 0);
        if (count <= 0) return;

        for (var i = 0; i < count; i++)
        {
            var message = $"Ping message from {UserId} to {TargetUserId} -> Count: {i}";
            Chat(TargetUserId, message);
        }
    }

    private void Chat(string targetUserId, string message)
    {
        var chat = new ChatNotifyReq { TargetUserId = targetUserId, Message = message };
        Send(chat);
    }
    
    
}
