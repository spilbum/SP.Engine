using System;
using System.Reflection;
using SP.Core.Fiber;
using SP.Core.Logging;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server.Connector;

public class ConnectorFiber : IDisposable
{
    private readonly IFiber _fiber;
    private readonly IScheduler _globalScheduler;
    private readonly IDisposable _tickTimer;
    private readonly ILogger _logger;
    private ConnectorBase _connector;
    private bool _disposed;
    
    public string Name => _connector?.Name;
    public string Host => _connector?.Host;
    public int Port => _connector?.Port ?? 0;
    
    public IConnector Connector => _connector;

    public ConnectorFiber(IFiber fiber, IScheduler globalScheduler, ILogger logger, TimeSpan tickPeriod)
    {
        _fiber = fiber;
        _globalScheduler = globalScheduler;
        _logger = logger;
        _tickTimer = globalScheduler.Schedule(fiber, Tick, TimeSpan.Zero, tickPeriod);
    }

    private void Tick()
    {
        try
        {
            _connector?.Tick();
        }
        catch (Exception ex)
        {
            _logger.Error("Connector '{0}' update failed: {1}/r/n{2}", Name, ex.Message, ex.StackTrace);
        }
    }

    public bool RegisterConnector(Assembly[] assemblies, ConnectorConfig config, IConnector baseConnector)
    {
        if (_connector != null) return false;
        var connector = (ConnectorBase)baseConnector;
        if (!connector.Initialize(assemblies, config, _fiber, _globalScheduler, _logger))
        {
            _logger.Error("Connector '{0}' initialize failed", baseConnector.Name);
            return false;
        }

        _connector = connector;
        return true;
    }

    public void Start()
    {
        if (_connector == null) throw new InvalidOperationException("Connector not initialized");
        _connector.Connect();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _tickTimer.Dispose();
        _connector?.Dispose();
        GC.SuppressFinalize(this);
    }
}
