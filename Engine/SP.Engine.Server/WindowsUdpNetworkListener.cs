using System;
using System.Net.Sockets;
using SP.Engine.Runtime;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server;

internal sealed class WindowsUdpNetworkListener(ListenerInfo info, IEngineConfig config) 
    : UdpNetworkListenerBase(info, config)
{
    private Socket _socket;

    public override bool Start()
    {
        try
        {
            InitializePool();
            _socket = new Socket(EndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
            _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _socket.SendBufferSize = Config.Network.SendBufferSize;
            _socket.ReceiveBufferSize = Config.Network.ReceiveBufferSize;
            
            const int SIO_UDP_CONNRESET = -1744830452;
            var inValue = new byte[] {0};
            var outValue = new byte[] {0};
            _socket.IOControl(SIO_UDP_CONNRESET, inValue, outValue);
            
            _socket.Bind(EndPoint);

            var initialLanes = Math.Clamp(Config.Session.MaxConnections / 4, 64, 512);
            for (var i = 0; i < initialLanes; i++)
            {
                if (!ReceiveArgsPool.TryRent(out var e)) break;
                e.Completed += OnReceiveCompleted;
                StartReceive(_socket, e);
            }
            return true;
        }
        catch (Exception ex)
        {
            OnError(ex);
            return false;
        }
    }

    public override void Stop()
    {
        if (IsStopping) return;
        IsStopping = true;
        
        _socket?.SafeClose();
        ReceiveArgsPool?.Dispose();
        OnStopped();
    }
}
