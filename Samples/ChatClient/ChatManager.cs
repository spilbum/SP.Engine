using System.Collections.Concurrent;
using Common.Protocol.CC2CS;
using Microsoft.Extensions.Logging;
using SP.Core.Logging;
using SP.Engine.Client;

namespace ChatClient;

public static class NetPeerExtensions
{
    public static UserPeer Build(this NetPeerBuilder @this, string userId, string targetUserId)
    {
        var peer = new UserPeer(userId, targetUserId);
        if (!@this.TryBuild(peer))
        {
            throw new InvalidOperationException("Failed to initialize peer.");
        }
        
        return peer;
    }
}

public class ChatManager
{
    private static readonly ILogger Logger = LogManager.GetLogger<ChatManager>();
    private readonly ConcurrentBag<UserPeer> _peers = [];
    private string? _host;
    private int _port;
    
    public bool IsRunning { get; private set; }

    public async Task StartConnectAsync(
        string host,
        int port,
        int count,
        string myGroup,
        string targetGroup,
        int delayMs,
        CancellationToken ct)
    {
        IsRunning = true;
        _host = host;
        _port = port;

        _ = Task.Run(UpdateLoop, ct);
        
        await AddConnectAsync(count, myGroup, targetGroup, delayMs, ct);
    }

    public async Task AddConnectAsync(int count, string myGroup, string targetGroup, int delayMs, CancellationToken ct)
    {
        if (!IsRunning) return;
        if (string.IsNullOrWhiteSpace(_host)) return;

        try
        {
            for (var i = 1; i <= count; i++)
            {
                var userId = $"User_{myGroup}_{i:D3}";
                var targetUserId = $"User_{targetGroup}_{i:D3}";
                
                var peer = NetPeerBuilder.Create()
                    .WithAutoPing(true, 2)
                    .AddAssembly(typeof(ChatNotifyReq).Assembly)
                    .WithEntryAssembly()
                    .Build(userId, targetUserId);
                
                peer.Connect(_host, _port);
                _peers.Add(peer);
                
                if (delayMs > 0)
                    await Task.Delay(delayMs, ct);
            }
            
            Logger.Info("Successfully initiated connection pool for {0} clients. Group: {1}", count, myGroup);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Failed during connection allocation process.");
        }
    }

    private async Task UpdateLoop()
    {
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };

        while (IsRunning)
        {
            try
            {
                if (!_peers.IsEmpty)
                {
                    Parallel.ForEach(_peers, parallelOptions, peer =>
                    {
                        try
                        {
                            peer.Tick();

                            if (peer.IsConnected)
                            {
                                peer.ProcessPendingChat();
                            }
                        }
                        catch (Exception ex)
                        {
                            Logger.Error("Tick Processing Exception: {0}\n{1}", ex.Message, ex.StackTrace);
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                Logger.Error("UpdateLoop pipeline fault: {0}", ex.Message);
            }

            await Task.Delay(10);
        }
    }
    
    private readonly object _lock = new();

    public void StartChatTest(int targetCount, int period, int batchCount)
    {
        lock (_lock)
        {
            var targets = _peers.Where(p => !p.IsSendingChat).Take(targetCount).ToList();
            foreach (var peer in targets)
            {
                peer.StartChatTest(period, batchCount);
            }
            
            Logger.Info("S2S Cross Chat stress test activated for {0} peers. (Interval: {1}ms, Burst: {2}",
                targets.Count, period, batchCount);
        }
    }

    public void StopChatTest()
    {
        lock (_lock)
        {
            foreach (var peer in _peers) peer.StopChatTest();
            Logger.Info("All active chat scheduler sequences suspended.");
        }
    }

    public void Stop()
    {
        IsRunning = false;
        foreach (var peer in _peers) peer.Close();
        Logger.Info("All simulated client endpoints successfully closed.");
    }
}
