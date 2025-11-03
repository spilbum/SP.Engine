using System;
using System.Threading;
using SP.Core.Fiber;
using SP.Core.Logging;
using SP.Engine.Client;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Configuration;
using EngineConfigBuilder = SP.Engine.Client.Configuration.EngineConfigBuilder;

namespace SP.Engine.Server.Connector;

public interface IConnector
{
    string Name { get; }
    string Host { get; }
    int Port { get; }
    bool Initialize(ILogger logger, IFiberScheduler scheduler, ConnectorConfig config);
    void Connect();
    void Close();
    void Update();
    bool Send(IProtocolData data);
}

public abstract class BaseConnector : BaseNetPeer, IConnector, ICommandContext
{
    private volatile int _connecting;
    private IDisposable _reconnectSchedule;
    private IFiberScheduler _scheduler;

    TProtocol ICommandContext.Deserialize<TProtocol>(IMessage message)
    {
        return message.Deserialize<TProtocol>(Encryptor, Compressor);
    }

    public string Name { get; private set; }
    public string Host { get; private set; }
    public int Port { get; private set; }

    public virtual bool Initialize(ILogger logger, IFiberScheduler scheduler, ConnectorConfig config)
    {
        if (string.IsNullOrEmpty(config.Host) || 0 >= config.Port)
        {
            logger.Error("Invalid connector config. host={0}, port={1}", config.Host,
                config.Port);
            return false;
        }

        Name = config.Name;
        Host = config.Host;
        Port = config.Port;
        _scheduler = scheduler;

        try
        {
            var engineConfig = EngineConfigBuilder.Create()
                .WithAutoPing(config.EnableAutoPing, config.AutoPingIntervalSec)
                .WithConnectAttempt(config.MaxConnectAttempts, config.ConnectAttemptIntervalSec)
                .WithReconnectAttempt(config.MaxReconnectAttempts, config.ReconnectAttemptIntervalSec)
                .Build();

            base.Initialize(engineConfig, logger);

            Connected += OnConnected;
            Disconnected += OnDisconnected;
            return true;
        }
        catch (Exception e)
        {
            logger.Error(e);
            return false;
        }
    }

    public void Connect()
    {
        if (Interlocked.Exchange(ref _connecting, 1) == 1) return;

        try
        {
            Connect(Host, Port);
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
        }
    }

    public virtual void Update()
    {
        Tick();
    }

    private void OnConnected(object sender, EventArgs e)
    {
        Interlocked.Exchange(ref _connecting, 0);
        _reconnectSchedule?.Dispose();
    }

    private void OnDisconnected(object sender, EventArgs e)
    {
        Interlocked.Exchange(ref _connecting, 0);
        _reconnectSchedule ??= _scheduler.Schedule(
            Connect,
            Host,
            Port,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(Config.ReconnectAttemptIntervalSec));
    }
}
