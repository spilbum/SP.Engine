using System;
using System.Collections.Generic;
using System.Reflection;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server;

public class EngineBuilder<TEngine> where TEngine : EngineBase, new()
{
    private string _name = typeof(TEngine).Name;
    private readonly List<Assembly> _assemblies = [];
    private readonly List<ListenerConfig> _listeners = [];
    private readonly List<ConnectorConfig> _connectors = [];

    private NetworkConfig _network = new();
    private SessionConfig _session = new();
    private PerfConfig _perf = new();
    
    private Action<TEngine> _setupAction;
    
    private EngineBuilder() { }

    public static EngineBuilder<TEngine> Create() => new();
    
    public EngineBuilder<TEngine> SetName(string name)
    {
        _name = name;
        return this;
    }

    /// <summary>
    /// 프로토콜 및 커맨드가 포함된 어셈블리를 추가합니다.
    /// </summary>
    public EngineBuilder<TEngine> AddAssembly(Assembly assembly)
    {
        if (assembly != null && !_assemblies.Contains(assembly))
            _assemblies.Add(assembly);
        return this;
    }
    
    public EngineBuilder<TEngine> Listen(int port, string ip = "Any", SocketMode mode = SocketMode.Tcp, int backlog = 1024)
    {
        _listeners.Add(new ListenerConfig { Port = port, Ip = ip, Mode = mode, BackLog = backlog });
        return this;
    }

    /// <summary>
    /// 외부 서버로의 연결 설정을 추가합니다.
    /// </summary>
    public EngineBuilder<TEngine> AddConnector(
        string name,
        string host, 
        int port,
        Func<ConnectorConfig, ConnectorConfig> configure = null)
    {
        var config = new ConnectorConfig { Name = name, Host = host, Port = port };
        if (configure != null)
        {
            config = configure(config);
        }
        
        _connectors.Add(config);
        return this;
    }

    public EngineBuilder<TEngine> ConfigureNetwork(Func<NetworkConfig, NetworkConfig> configure)
    {
        _network = configure?.Invoke(_network) ?? _network;
        return this;
    }

    public EngineBuilder<TEngine> ConfigureSession(Func<SessionConfig, SessionConfig> configure)
    {
        _session = configure?.Invoke(_session) ?? _session;
        return this;
    }

    public EngineBuilder<TEngine> ConfigurePerformance(Func<PerfConfig, PerfConfig> configure)
    {
        _perf = configure?.Invoke(_perf) ?? _perf;
        return this;
    }

    public EngineBuilder<TEngine> Setup(Action<TEngine> action)
    {
        _setupAction = action;
        return this;
    }

    /// <summary>
    /// 설정을 바탕으로 엔진을 생성하고 초기화합니다.
    /// </summary>
    public TEngine Build()
    {
        var config = new EngineConfig
        {
            Listeners = _listeners,
            Connectors = _connectors,
            Network = _network,
            Session = _session,
            Perf = _perf
        };
        
        var engine = new TEngine();

        var assembly = Assembly.GetEntryAssembly();
        if (assembly != null && !_assemblies.Contains(assembly))
            _assemblies.Add(assembly);

        if (!engine.InternalInitialize(_assemblies.ToArray(), _name, config))
        {
            throw new InvalidOperationException("Engine initialization failed. Check logs for details.");
        }
        
        _setupAction?.Invoke(engine);

        return engine;
    }
}
