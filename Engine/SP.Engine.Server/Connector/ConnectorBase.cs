using System;
using System.Reflection;
using System.Threading;
using SP.Core.Fiber;
using SP.Core.Logging;
using SP.Engine.Client;
using SP.Engine.Client.Configuration;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Networking;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server.Connector;

public abstract class ConnectorBase : NetPeerBase, IConnector, ICommandContext
{
    private volatile int _connecting;
    private IDisposable _reconnectTimer;
    private IFiber _fiber;
    private IScheduler _globalScheduler;

    public string Name { get; private set; }
    public string Host { get; private set; }
    public int Port { get; private set; }
    
    public virtual bool Initialize(Assembly[] assemblies, ConnectorConfig config, IFiber fiber, IScheduler globalScheduler, ILogger logger)
    {
        if (config == null || string.IsNullOrEmpty(config.Host) || 0 >= config.Port)
        {
            logger.Error("Invalid connector config. host={0}, port={1}", config?.Host, config?.Port);
            return false;
        }

        Name = config.Name;
        Host = config.Host;
        Port = config.Port;
        
        _fiber = fiber;
        _globalScheduler = globalScheduler;

        try
        {
            var builder = NetPeerBuilder.Create()
                .WithLogger(logger)
                .WithAutoPing(config.EnableAutoPing, config.AutoPingIntervalSec)
                .WithConnectionAttempts(config.MaxConnectAttempts, config.ConnectAttemptIntervalSec)
                .WithReconnectAttempts(config.MaxReconnectAttempts, config.ReconnectAttemptIntervalSec);
            
            foreach (var assembly in assemblies) builder.WithAssembly(assembly);
 
            if (!builder.Apply(this))
                return false;

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

    public bool Connect()
    {
        if (Interlocked.Exchange(ref _connecting, 1) == 1) return false;
        try
        {
            Connect(Host, Port);
            return true;
        }
        catch (Exception ex)
        {
            Logger.Error(ex);
            return false;
        }
    }
    
    private void OnConnected(object sender, EventArgs e)
    {
        Interlocked.Exchange(ref _connecting, 0);
        _reconnectTimer?.Dispose();
    }

    private void OnDisconnected(object sender, EventArgs e)
    {
        Interlocked.Exchange(ref _connecting, 0);

        // 재연결 타이머 등록
        _reconnectTimer ??= _globalScheduler.Schedule(
            _fiber,
            Connect,
            Host,
            Port,
            TimeSpan.Zero,
            TimeSpan.FromSeconds(Config.ReconnectAttemptIntervalSec)); 
    }
    
    TProtocol ICommandContext.Deserialize<TProtocol>(IMessage message)
    {
        return message.Deserialize<TProtocol>(Encryptor, Compressor);
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _reconnectTimer?.Dispose();
            _fiber?.Dispose();
        }
        
        base.Dispose(disposing);
    }
}
