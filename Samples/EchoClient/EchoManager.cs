using System.Collections.Concurrent;
using SP.Core.Logging;
using SP.Engine.Client.Configuration;

namespace EchoClient;

public class EchoManager(ILogger logger)
{
    private readonly ConcurrentBag<EchoClient> _clients = [];
    private string? _host;
    private int _port;

    public bool IsRunning { get; private set; }
    public ILogger Logger => logger;

    public void Test_Reconnect(int delayMs)
    {
        if (delayMs == 0)
        {
            foreach (var client in _clients) client.Test_Reconnect();
        }
        else
        {
            _ = Task.Run(async () =>
            {
                foreach (var client in _clients)
                {
                    client.Test_Reconnect();
                    await Task.Delay(delayMs);
                }
            });
        }
    }
    
    public async Task StartConnectAsync(string host, int port, int count, int delayMs, CancellationToken token)
    {
        IsRunning = true;
        _host = host;
        _port = port;

        _ = Task.Run(UpdateLoop, token);
        await AddConnectAsync(count, delayMs, token);
    }

    public void Stop()
    {
        IsRunning = false;
        foreach (var client in _clients) client.Close();
    }

    public async Task AddConnectAsync(int count, int delayMs, CancellationToken token)
    {
        if (!IsRunning) return;

        try
        {
            for (var i = 0; i < count; i++)
            {
                var client = NetPeerBuilder.Create()
                    .WithLogger(logger)
                    .WithAutoPing(true, 2)
                    .Build<EchoClient>();
            
                client.Connect(_host, _port);
                _clients.Add(client);
            
                if (delayMs > 0) await Task.Delay(delayMs, token);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
        }
    }

    private async Task UpdateLoop()
    {
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        
        while (IsRunning)
        {
            if (!_clients.IsEmpty)
            {
                Parallel.ForEach(_clients, parallelOptions, client =>
                {
                    try
                    {
                        client.Tick();

                        if (client.IsConnected)
                        {
                            client.ProcessPendingEcho();
                        }
                    }
                    catch (Exception e)
                    {
                        logger.Error($"Tick Error: {e.Message}/r/nStacktrace: {e.StackTrace}");
                    }
                });
            }
            
            await Task.Delay(10);
        }
    }
    
    private readonly object _lock = new();

    public void StartEchoTest(int targetCount, string sendType, int period, int batchCount)
    {
        lock (_lock)
        {
            var targets = _clients.Take(targetCount).ToList();
            
            foreach (var client in targets)
            {
                client.StartEcho(sendType, period, batchCount);
            }
            
            Logger.Info($"Echo test started for {targets.Count} clients (SendType: {sendType})");
        }
    }

    public void StopEchoTest()
    {
        lock (_lock)
        {
            foreach (var client in _clients) client.StopEcho();
            logger.Info("All echo tests stopped");
        }
    }
}

