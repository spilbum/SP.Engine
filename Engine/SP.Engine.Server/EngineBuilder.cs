using System;
using System.Collections.Generic;
using System.Reflection;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server;

public class EngineBuilder<TEngine> where TEngine : EngineBase, new()
{
    private string _category;
    private string _name = typeof(TEngine).Name;
    private readonly List<Assembly> _assemblies = [];
    private readonly List<ListenerConfig> _listeners = [];
    private readonly List<S2SClientConfig> _s2sClientConfigs = [];

    private readonly List<Action<EngineConfig>> _configActions = [];
    
    private Action<TEngine> _setupAction;
    private bool _isBuilt;
    
    private EngineBuilder() { }

    public static EngineBuilder<TEngine> Create() => new();

    private EngineBuilder<TEngine> Configure(Action<EngineConfig> action)
    {
        if (_isBuilt) throw new InvalidOperationException("Builder cannot be modified after Build().");
        if (action != null) _configActions.Add(action);
        return this;
    }

    public EngineBuilder<TEngine> SetCategory(string category)
    {
        if (string.IsNullOrEmpty(category)) throw new ArgumentNullException(nameof(category));
        _category = category;
        return this;
    }
    
    public EngineBuilder<TEngine> SetName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Name cannot be null or empty.", nameof(name));
        _name = name;
        return this;
    }
    
    public EngineBuilder<TEngine> ConfigureNetwork(Action<NetworkConfig> configure)
        => Configure(c => configure?.Invoke(c.Network));

    public EngineBuilder<TEngine> ConfigureSession(Action<SessionConfig> configure)
        => Configure(c => configure?.Invoke(c.Session));

    public EngineBuilder<TEngine> ConfigurePerformance(Action<PerfConfig> configure)
        => Configure(c => configure?.Invoke(c.Perf));

    public EngineBuilder<TEngine> Listen(int port, string ip = "Any", SocketMode mode = SocketMode.Tcp, int backlog = 1024)
    {
        _listeners.Add(new ListenerConfig { Port = port, Ip = ip, Mode = mode, BackLog = backlog });
        return this;
    }
    
    public EngineBuilder<TEngine> AddS2SClient(
        string name,
        string host, 
        int port,
        Action<S2SClientConfig> configure = null)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Connector name cannot be empty.", nameof(name));
        if (string.IsNullOrWhiteSpace(host)) throw new ArgumentException("Host cannot be empty.", nameof(host));
        
        var config = new S2SClientConfig { Name = name, Host = host, Port = port };
        configure?.Invoke(config);
        _s2sClientConfigs.Add(config);
        return this;
    }
    
    public EngineBuilder<TEngine> AddAssembly(Assembly assembly)
    {
        if (assembly != null && !_assemblies.Contains(assembly)) _assemblies.Add(assembly);
        return this;
    }

    public EngineBuilder<TEngine> WithEntryAssembly()
    {
        var assembly = Assembly.GetEntryAssembly()
            ?? throw new InvalidOperationException("Could not resolve EntryAssembly.");
        if (!_assemblies.Contains(assembly)) _assemblies.Add(assembly);
        return this;
    }
    
    public EngineBuilder<TEngine> Setup(Action<TEngine> action)
    {
        if (action != null)
        {
            _setupAction = _setupAction == null 
                ? action 
                : Delegate.Combine(_setupAction, action) as Action<TEngine>;
        }
        return this;
    }

    /// <summary>
    /// 설정을 바탕으로 엔진을 생성하고 초기화합니다.
    /// </summary>
    public TEngine Build()
    {
        if (_isBuilt) throw new InvalidOperationException("This builder has already used to build an engine instance.");
        
        if (string.IsNullOrEmpty(_category)) throw new InvalidOperationException("Category cannot be null or empty.");
        
        if (_assemblies.Count == 0)
        {
            throw new InvalidOperationException(
                "No assemblies configured for command discovery. Did you forget to call WithAssembly() or WithEntryAssembly()?");
        }
        
        var config = new EngineConfig
        {   
            Listeners = [.._listeners],
            S2SClients = [.._s2sClientConfigs]
        };

        foreach (var action in _configActions)
        {
            action(config);
        }
        
        var engine = new TEngine();
        if (!engine.InternalInitialize([.._assemblies], _category, _name, config))
        {
            throw new InvalidOperationException("Engine initialization failed. Check logs for details.");
        }
        
        _setupAction?.Invoke(engine);
        _isBuilt = true;

        return engine;
    }
}
