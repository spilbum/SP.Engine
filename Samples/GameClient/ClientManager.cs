using System.Collections.Concurrent;
using SP.Core.Logging;
using SP.Engine.Client.Configuration;

namespace GameClient;

public class ClientManager(EngineConfig config, ILogger logger)
{
    private readonly ConcurrentBag<Client> _clients = [];
    private string? _host;
    private int _port;

    public bool IsRunning { get; private set; }
    public ILogger Logger => logger;

    public void Test_Reconnect()
    {
        foreach (var client in _clients)
        {
            client.Test_Reconnect();
        }
    }
    
    public async Task StartConnectAsync(string host, int port, int count, int delayMs)
    {
        IsRunning = true;
        _host = host;
        _port = port;

        _ = Task.Run(UpdateLoop);
        await AddConnectAsync(count, delayMs);
    }

    public void Stop()
    {
        IsRunning = false;
        foreach (var client in _clients) client.Close();
    }

    public async Task AddConnectAsync(int count, int delayMs)
    {
        if (!IsRunning) return;

        for (var i = 0; i < count; i++)
        {
            var client = new Client(config, logger);
            client.Connect(_host, _port);
            _clients.Add(client);
            
            if (delayMs > 0) await Task.Delay(delayMs);
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
