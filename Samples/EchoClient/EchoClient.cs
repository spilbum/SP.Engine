using Common;
using SP.Common.Logging;
using SP.Engine.Client;
using SP.Engine.Client.Configuration;
using SP.Engine.Runtime.Handler;
using SP.Engine.Runtime.Protocol;

namespace EchoClient;

public class EchoClient
{
    private NetPeer? _peer;
    private readonly Dictionary<ushort, ProtocolMethodInvoker> _invokerDict = new();

    public ILogger Logger => _peer?.Logger ?? throw new NullReferenceException(nameof(_peer));
    public Action<S2CProtocolData.TcpEchoAck>? TcpEchoAckHandler { get; set; }
    public Action<S2CProtocolData.UdpEchoAck>? UdpEchoAckHandler { get; set; }
    
    public EchoClient(ILogger logger, EngineConfig config)
    {
        _peer = new NetPeer(config, logger);
        _peer.Connected += OnConnected;
        _peer.Disconnected += OnDisconnected;
        _peer.Error += OnError;
        _peer.MessageReceived += OnMessageReceived;
        _peer.Offline += OnOffline;
        _peer.StateChanged += OnStateChanged;

        if (!ProtocolMethodInvoker.LoadInvokers(GetType())
                .All(invoker => _invokerDict.TryAdd(invoker.Id, invoker)))
        {
            throw new Exception("LoadInvokers failed.");
        }
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

    private void OnMessageReceived(object? sender, MessageReceivedEventArgs e)
    {
        var message = e.Message;
        if (_invokerDict.TryGetValue(message.Id, out var invoker))
            invoker.Invoke(this, message, _peer?.Encryptor, _peer?.Compressor);
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

    [ProtocolMethod(S2CProtocol.TcpEchoAck)]
    private void _(S2CProtocolData.TcpEchoAck packet)
    {
        TcpEchoAckHandler?.Invoke(packet);
    }
    
    [ProtocolMethod(S2CProtocol.UdpEchoAck)]
    private void _(S2CProtocolData.UdpEchoAck packet)
    {
        UdpEchoAckHandler?.Invoke(packet);
    }
}
