using SP.Common.Logging;
using SP.Engine.Client;
using SP.Engine.Client.Configuration;

namespace EchoClient;

public class EchoClient : BaseNetPeer
{
    public EchoClient(EngineConfig config, ILogger logger)
    {
        Initialize(config, logger);
        
        Connected += OnConnected;
        Disconnected += OnDisconnected;
        Error += OnError;
        Offline += OnOffline;
        StateChanged += OnStateChanged;
    }
    
    private void OnStateChanged(object? sender, StateChangedEventArgs e)
    {
        Logger.Debug("[OnStateChanged] {0} -> {1}", e.OldState, e.NewState);
    }

    private void OnOffline(object? sender, EventArgs e)
    {
        Logger.Debug("[OnOffline] Offline");
    }

    private void OnError(object? sender, ErrorEventArgs e)
    {
        Logger.Error(e.GetException());
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        Logger.Info("[OnDisconnected] Disconnected");
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        Logger.Info("[OnConnected] Connected");
    }
}
