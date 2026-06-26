using System;
using System.Reflection;
using System.Threading;
using SP.Core.Fiber;
using SP.Engine.Client;
using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.S2S;
using SP.Engine.Runtime;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Protocol;
using SP.Engine.Server.S2S.Command;

namespace SP.Engine.Server.S2S;

public class S2SClient(EngineBase engine) : NetPeerBase(PeerKind.Server), IS2SClient
{
    private IDisposable _reconnectTimer;
    private IDisposable _tickTimer;
    private ThreadFiber _fiber;
    private IScheduler _scheduler;
    private int _connecting;
    private int _enableAutoReconnect = 1;
    private Action<S2SClient> _connected;
    private Action<S2SClient> _disconnected;
    
    public string Name { get; private set; }
    public string Host { get; private set; }
    public int Port { get; private set; }
    
    public new event Action<S2SClient> Connected
    {
        add => _connected += value;
        remove => _connected -= value;
    }
    
    public new event Action<S2SClient> Disconnected
    {
        add => _disconnected += value;
        remove => _disconnected -= value;
    }

    public bool Initialize(Assembly[] assemblies, S2SClientConfig config)
    {
        if (config == null || string.IsNullOrEmpty(config.Host) || 0 >= config.Port)
        {
            engine.Logger.Error("Invalid connector config. host={0}, port={1}", config?.Host, config?.Port);
            return false;
        }

        Name = config.Name;
        Host = config.Host;
        Port = config.Port;

        _scheduler = engine.GlobalScheduler;
        _fiber = new ThreadFiber($"S2SFiber-{config.Name}", onError: OnFiberException);
        var logger = engine.Logger;
        
        try
        {
            var builder = NetPeerBuilder.Create()
                .WithLogger(logger)
                .WithAutoPing(config.EnableAutoPing, config.AutoPingIntervalSec)
                .WithConnectAttempts(config.MaxConnectAttempts, config.ConnectAttemptIntervalSec)
                .WithReconnectAttempts(config.MaxReconnectAttempts, config.ReconnectAttemptIntervalSec)
                .WithEntryAssembly();
            
            foreach (var assembly in assemblies) builder.AddAssembly(assembly);
 
            if (!builder.TryInitialize(this))
            {
                _fiber.Dispose();
                return false;
            }
            
            RegisterEngineCommand<S2SConnectAckHandler>(ProtocolId.S2S.S2SConnectAck);

            _tickTimer = _scheduler.Schedule(
                _fiber, 
                Tick, 
                TimeSpan.Zero,
                TimeSpan.FromMilliseconds(config.UpdateIntervalMs));

            base.Connected += OnConnected;
            base.Disconnected += OnDisconnected;
            return true;
        }
        catch (Exception ex)
        {
            _fiber.Dispose();
            logger.Error(ex);
            return false;
        }
    }

    private void OnFiberException(Exception ex)
    {
        switch (ex)
        {
            case FiberException fe:
                Logger.Error(fe, "Fiber '{0}' execute failed. Job: {1}", fe.FiberName, fe.Job?.Name);
                break;
            default:
                Logger.Error(ex);
                break;
        }
    }

    internal void S2SConnectCompleted()
    {
        Interlocked.Exchange(ref _connecting, 0);
        Volatile.Write(ref _enableAutoReconnect, 1);
        _connected?.Invoke(this);
    }

    public void Connect()
    {
        if (Interlocked.Exchange(ref _connecting, 1) == 1)
            return;
        
        try
        {
            Connect(Host, Port);
        }
        catch (Exception ex)
        {
            Interlocked.Exchange(ref _connecting, 0);
            Logger.Error(ex);
        }
    }
    
    private void OnConnected(object sender, EventArgs e)
    {
        Interlocked.Exchange(ref _connecting, 0);
        Volatile.Write(ref _enableAutoReconnect, 0);
        _reconnectTimer?.Dispose();
        _reconnectTimer = null;
        
        using var scope = ProtocolScope<S2SConnectReq>.Rent();
        scope.Protocol.Category = engine.Category;
        InternalSend(scope.Protocol);
    }

    private void OnDisconnected(object sender, EventArgs e)
    {
        Interlocked.Exchange(ref _connecting, 0);
        if (Volatile.Read(ref _enableAutoReconnect) == 1)
        {
            _fiber.Enqueue(StartReconnectTimer);
        }
        else
        {
            _disconnected?.Invoke(this);
        }
    }

    private void StartReconnectTimer()
    {
        if (_reconnectTimer != null) return;
        
        var delay = TimeSpan.FromSeconds(
            Config.ConnectAttemptIntervalSec > 0 ? Config.ConnectAttemptIntervalSec : 1);
        
        _reconnectTimer = _scheduler.Schedule(
            _fiber,
            Connect,
            delay,
            TimeSpan.FromSeconds(Config.ReconnectAttemptIntervalSec));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            base.Connected -= OnConnected;
            base.Disconnected -= OnDisconnected;
            _tickTimer?.Dispose();
            _reconnectTimer?.Dispose();
            _fiber?.Dispose();
        }
        
        base.Dispose(disposing);
    }
}
