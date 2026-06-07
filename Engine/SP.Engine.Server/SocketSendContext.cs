using System;
using System.Net.Sockets;

namespace SP.Engine.Server;

public sealed class SocketSendContext : IDisposable
{
    public SocketAsyncEventArgs SocketEventArgs { get; }
    public byte[] Buffer { get; private set; }
    public int OriginOffset { get; }
    public int BufferSize { get; private set; }
    
    public SendRingBuffer RingBuffer { get; }

    public SocketSendContext(
        SocketAsyncEventArgs e, 
        byte[] globalBuffer,
        int offset,
        int bufferSize)
    {
        SocketEventArgs = e;
        Buffer = globalBuffer;
        OriginOffset = offset;
        BufferSize = bufferSize;
        RingBuffer = new SendRingBuffer(globalBuffer, offset, bufferSize);
        SocketEventArgs.Completed += OnSendCompleted;
    }

    public void Initialize(INetworkSession session)
    {
        SocketEventArgs.UserToken = session;
    }

    private static void OnSendCompleted(object sender, SocketAsyncEventArgs e)
    {
        if (sender is not TcpNetworkSession ns) return;
        ns.ProcessSend(e);
    }

    public void Reset()
    {
        RingBuffer.Clear();
        SocketEventArgs.UserToken = null;
        SocketEventArgs.RemoteEndPoint = null;
        SocketEventArgs.SetBuffer(OriginOffset, 0);
    }

    public void Dispose()
    {
        SocketEventArgs.Dispose();
    }
}

internal class SendContextFactory(int bufferSize) : IPoolObjectFactory<SocketSendContext>
{
    public SocketSendContext[] Create(int size)
    {
        var globalBuffer = GC.AllocateArray<byte>(bufferSize * size);
        var contexts = new SocketSendContext[size];

        for (var i = 0; i < size; i++)
        {
            var e = new SocketAsyncEventArgs();
            var offset = i * bufferSize;
            contexts[i] = new SocketSendContext(e, globalBuffer, offset, bufferSize);
        }
        
        return contexts;
    }
}
