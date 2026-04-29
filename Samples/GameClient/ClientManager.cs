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

    public void StartEchoTest(string type, int period, int batch)
    {
        var cts = new CancellationTokenSource();
        foreach (var client in _clients)
        {
            _ = client.RunSender(type, period, batch, cts.Token);
        }
    }

    public void StopAll()
    {
        IsRunning = false;
        foreach (var client in _clients) client.Close();
        _clients.Clear();
    }
}
