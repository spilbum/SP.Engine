using System;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SP.Engine.Runtime;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server;

internal sealed class UnixUdpNetworkListener(ListenerInfo info, IEngineConfig config) 
    : UdpNetworkListenerBase(info, config)
{
    private Socket[] _sockets;
    
    public override bool Start()
    {
        try
        {
            InitializePool();
            var socketCount = Environment.ProcessorCount;
            _sockets = new Socket[socketCount];

            for (var i = 0; i < socketCount; i++)
            {
                var s = CreateSocket();
                s.ReceiveBufferSize = 1024 * 1024 * 32;
                s.Bind(EndPoint);
                _sockets[i] = s;

                var lanesPerSocket = Math.Clamp(Config.Session.MaxConnections / socketCount / 2, 8, 128);
                for (var j = 0; j < lanesPerSocket; j++)
                {
                    if (!ReceiveArgsPool.TryRent(out var e)) break;
                    e.Completed += OnReceiveCompleted;
                    StartReceive(s, e);
                }
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
        
        if (_sockets != null)
            foreach (var s in _sockets) s?.SafeClose();
        
        ReceiveArgsPool?.Dispose();
        OnStopped();
    }

    private Socket CreateSocket()
    {
        var socket = new Socket(EndPoint.AddressFamily, SocketType.Dgram, ProtocolType.Udp);

        int level, opt;
        if (OperatingSystem.IsMacOS())
        {
            level = 0xffff;
            opt = 0x0200;
        }
        else
        {
            level = 1;
            opt = 15;
        }

        Span<byte> optionValue = stackalloc byte[sizeof(int)];

        var value = 1;
        MemoryMarshal.Write(optionValue, in value);
        
        socket.SetRawSocketOption(level, opt, optionValue);
        return socket;
    }
}
