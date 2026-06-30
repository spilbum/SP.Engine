using System.Collections.Concurrent;
using Common.Protocol.EC2ES;
using SP.Core.Logging;
using SP.Engine.Client;

namespace EchoClient;

public class TestManager(ILogger logger)
{
    private readonly ConcurrentBag<UserPeer> _clients = [];
    private string? _host;
    private int _port;

    public bool IsRunning { get; private set; }

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
                    .AddAssembly(typeof(TcpEchoReq).Assembly)
                    .WithEntryAssembly()
                    .Build<UserPeer>();
            
                client.Connect(_host, _port);
                _clients.Add(client);
            
                if (delayMs > 0) await Task.Delay(delayMs, token);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex);
        }
    }

    private async Task UpdateLoop()
    {
        var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount };
        
        while (IsRunning)
        {
            try
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
                                client.ProcessSend();
                            }
                        }
                        catch (Exception e)
                        {
                            logger.Error($"Tick Error: {e.Message}/r/nStacktrace: {e.StackTrace}");
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                logger.Error("UpdateLoop failed: {0}", ex.Message);
            }

            await Task.Delay(10);
        }
    }
    
    private readonly object _lock = new();

    public void StartTest(int targetCount, string sendType, int period, int batchCount)
    {
        lock (_lock)
        {
            var targets = _clients.Where(c => !c.IsRunning).Take(targetCount).ToList();
            foreach (var client in targets)
            {
                client.StartTest(sendType, period, batchCount);
            }
            
            logger.Info($"Test started for {targets.Count} clients (SendType: {sendType})");
        }
    }

    public void StopTest()
    {
        lock (_lock)
        {
            foreach (var client in _clients) client.StopTest();
            logger.Info("All tests stopped");
        }
    }
}

