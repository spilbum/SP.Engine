using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Server;

public class UdpSocket : BaseNetworkSession, IUnreliableSender
{
    private readonly FragmentAssembler _assembler = new();
    private uint _fragSeq;
    private ushort _maxDataSize = 512;
    private readonly SocketSendBuffer _sendBuffer = new(1024 * 16);
    
    public UdpSocket(Socket client, IPEndPoint remoteEndPoint)
        : base(SocketMode.Udp, client)
    {
        RemoteEndPoint = remoteEndPoint;
        LocalEndPoint = (IPEndPoint)client.LocalEndPoint;
    }

    public IFragmentAssembler Assembler => _assembler;

    public bool TrySend(UdpMessage message)
    {
        using (message)
        {
            if (message.Size <= _maxDataSize)
            {
                if (!_sendBuffer.TryReserve(message.Size, out var segment, out var span)) return false;
                message.WriteTo(span);
                return TrySend(segment);
            }
            
            const int hSize = UdpHeader.ByteSize;
            const int fHeaderSize = FragmentHeader.ByteSize;
            var fragId = AllocateFragId();
            var maxBodyPerFrag = _maxDataSize - hSize - fHeaderSize;
            var totalCount = (byte)Math.Ceiling((double)message.BodyLength / maxBodyPerFrag);

            for (byte index = 0; index < totalCount; index++)
            {
                var bodyOffset = index * maxBodyPerFrag;
                var fragLen = (ushort)Math.Min(message.BodyLength - bodyOffset, maxBodyPerFrag);
                var totalSize = hSize + fHeaderSize + fragLen;

                if (!_sendBuffer.TryReserve(totalSize, out var segment, out var span)) return false;

                message.WriteFragmentTo(span, fragId, index, totalCount, bodyOffset, fragLen);
                if (!TrySend(segment)) return false;
            }
        }
        
        return true;
    }

    private uint AllocateFragId()
    {
        return Interlocked.Increment(ref _fragSeq);
    }

    public void SetMaxFrameSize(ushort mtu)
    {
        _maxDataSize = (ushort)(mtu - 20 /* IP header size */ - 8 /* UDP header size*/);
    }

    public void UpdateRemoteEndPoint(IPEndPoint remoteEndPoint)
    {
        if (RemoteEndPoint != null && RemoteEndPoint.ToString() == remoteEndPoint.ToString())
            return;

        RemoteEndPoint = remoteEndPoint;
    }

    protected override void Send(SegmentQueue queue)
    {
        var e = new SocketAsyncEventArgs();
        e.Completed += OnSendCompleted;
        e.RemoteEndPoint = RemoteEndPoint;
        e.UserToken = queue;

        var item = queue[queue.Position];
        e.SetBuffer(item.Array, item.Offset, item.Count);

        var client = Client;
        if (client == null)
        {
            OnSendError(queue, CloseReason.SocketError);
            return;
        }

        if (!client.SendToAsync(e))
            OnSendCompleted(this, e);
    }

    private void ClearSocketAsyncEventArgs(SocketAsyncEventArgs e)
    {
        e.UserToken = null;
        e.Completed -= OnSendCompleted;
        e.Dispose();
    }

    private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
    {
        if (e.UserToken is not SegmentQueue queue)
            return;

        if (e.SocketError != SocketError.Success)
        {
            LogError(new SocketException((int)e.SocketError));
            ClearSocketAsyncEventArgs(e);
            OnSendError(queue, CloseReason.SocketError);
            return;
        }
        
        _sendBuffer.Release(e.BytesTransferred);

        ClearSocketAsyncEventArgs(e);

        var newPos = queue.Position + 1;
        if (newPos >= queue.Count)
        {
            OnSendCompleted(queue);
            return;
        }

        queue.SetPosition(newPos);
        Send(queue);
    }

    protected override bool TryValidateClosedBySocket(out Socket client)
    {
        client = null;
        return false;
    }

    protected override void OnClosed(CloseReason reason)
    {
        _sendBuffer.Dispose();
        base.OnClosed(reason);
    }
}
