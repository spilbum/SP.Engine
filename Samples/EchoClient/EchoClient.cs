using Common;
using SP.Common.Logging;
using SP.Engine.Client;
using SP.Engine.Client.Configuration;
using SP.Engine.Runtime.Protocol;

namespace EchoClient;

public class EchoClient
{
    private NetPeer? _peer;

    public ILogger Logger => _peer?.Logger ?? throw new NullReferenceException(nameof(_peer));
    
    public EchoClient(EngineConfig config, ILogger logger)
    {
        var assembly = GetType().Assembly;
        _peer = new NetPeer(config, assembly, logger);
        _peer.Connected += OnConnected;
        _peer.Disconnected += OnDisconnected;
        _peer.Error += OnError;
        _peer.Offline += OnOffline;
        _peer.StateChanged += OnStateChanged;
    }

    public void Open(string host, int port)
    {
        _peer?.Connect(host, port);
    }

    public void Close()
    {
        _peer?.Close();
    }

    public void Tick()
    {
        _peer?.Tick();
    }

    public void Send(IProtocol protocol)
    {
        _peer?.Send(protocol);
    }

    public void SendPing()
    {
        _peer?.SendPing();
    }

    public TrafficInfo GetTcpTrafficInfo() =>
        _peer?.GetTcpTrafficInfo() ?? throw new NullReferenceException(nameof(_peer));

    public TrafficInfo GetUdpTrafficInfo() =>
        _peer?.GetUdpTrafficInfo() ?? throw new NullReferenceException(nameof(_peer));

    public LatencyStats GetLatencyStats() => _peer?.LatencyStats ?? throw new NullReferenceException(nameof(_peer));

    private void OnStateChanged(object? sender, StateChangedEventArgs e)
    {
        _peer?.Logger.Debug("[OnStateChanged] {0} -> {1}", e.OldState, e.NewState);
    }

    private void OnOffline(object? sender, EventArgs e)
    {
        _peer?.Logger.Debug("[OnOffline] Offline");
    }

    private void OnError(object? sender, ErrorEventArgs e)
    {
        _peer?.Logger.Error(e.GetException());
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        _peer?.Logger.Info("[OnDisconnected] Disconnected");
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        _peer?.Logger.Info("[OnConnected] Connected");
    }
}
