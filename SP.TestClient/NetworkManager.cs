using System.Reflection;
using NetworkCommon;
using SP.Common;
using SP.Engine.Client;
using SP.Engine.Runtime.Handler;
using SP.Engine.Runtime.Protocol;

namespace SP.TestClient;

public class NetworkManager : Singleton<NetworkManager>
{
    private NetPeer? _netPeer;
    private readonly Dictionary<EProtocolId, ProtocolMethodInvoker> _invokerDict = new();
    
    public bool Connected => _netPeer?.State == ENetPeerState.Open;
    
    public bool Initialize()
    {
        _netPeer = new NetPeer();
        _netPeer.Connected += OnConnected;
        _netPeer.Disconnected += OnDisconnected;
        _netPeer.Offline += OnOffline;
        _netPeer.Error += OnError;
        _netPeer.MessageReceived += OnMessageReceived;

        foreach (var invoker in ProtocolMethodInvoker.LoadInvokers(GetType()))
            _invokerDict.Add(invoker.ProtocolId, invoker);
        
        return true;
    }

    public void Connect(string ip, int port)
    {
        _netPeer?.Open(ip, port);
    }

    public void Update()
    {
        _netPeer?.Update();
    }

    public void SendProtocol(IProtocolData protocol)
    {
        _netPeer?.Send(protocol);
    }

    public void Disconnect()
    {
        _netPeer?.Close();
    }

    private ProtocolMethodInvoker? GetInvoker(EProtocolId protocolId)
    {
        _invokerDict.TryGetValue(protocolId, out var invoker);
        return invoker;
    }

    private void OnMessageReceived(object? sender, MessageEventArgs e)
    {
        var message = e.Message;
        var invoker = GetInvoker(message.ProtocolId);
        invoker?.Invoke(this, message, _netPeer?.DhSharedKey);
    }

    private void OnError(object? sender, ErrorEventArgs e)
    {
        Console.WriteLine("Error message: {0}", e.GetException().Message);
    }

    private void OnOffline(object? sender, EventArgs e)
    {
        Console.WriteLine("OnOffline");
    }

    private void OnDisconnected(object? sender, EventArgs e)
    {
        Console.WriteLine("OnDisconnected");
    }

    private void OnConnected(object? sender, EventArgs e)
    {
        Console.WriteLine("OnConnected");
    }

    [ProtocolMethod(S2CProtocol.LoginAck)]
    private void OnLoginAck(S2CProtocol.Data.LoginAck loginAck)
    {
        Console.WriteLine("[{2:yyyy-MM-dd hh:mm:ss.fff}] LoginAck: {0}, {1}", loginAck.Uid, loginAck.SentTime, DateTime.UtcNow);
    }
}
