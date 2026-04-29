using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Core;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Server;

public class UdpNetworkSession : BaseNetworkSession, IUnreliableSender
{
    private readonly IObjectPool<SocketAsyncEventArgs> _udpArgsPool;
    private readonly SessionSendBuffer _sendBuffer = new(1024 * 32);
    private uint _fragSeq;
    private ushort _maxDataSize = 512;

    public UdpFragmentAssembler Assembler { get; private set; }

    public UdpNetworkSession(Socket client, IPEndPoint remoteEndPoint, IObjectPool<SocketAsyncEventArgs> udpArgsPool)
        : base (SocketMode.Udp, client)
    {
        RemoteEndPoint = remoteEndPoint;
        _udpArgsPool = udpArgsPool;
    }

    public void SetupAssembler(int cleanupTimeoutSec, int maxPendingMessageCount)
    {
        Assembler ??= new UdpFragmentAssembler(cleanupTimeoutSec, maxPendingMessageCount);
    }

    public bool TrySend(UdpMessage message)
    {
        if (IsClosed) return false;

        if (message.Size <= _maxDataSize)
        {
            if (!_sendBuffer.TryReserve(message.Size, out var segment, out var span)) return false;
            message.WriteTo(span);
            Send(segment);
            return true;
        }

        // MTU 초과 시 Fragment 분할 전송
        const int hSize = UdpHeader.ByteSize;
        const int fHeaderSize = UdpFragmentHeader.ByteSize;
        
        var fragId = Interlocked.Increment(ref _fragSeq);
        var maxBodyPerFrag = _maxDataSize - hSize - fHeaderSize;
        var totalCount = (byte)Math.Ceiling((double)message.BodyLength / maxBodyPerFrag);

        for (byte index = 0; index < totalCount; index++)
        {
            var bodyOffset = index * maxBodyPerFrag;
            var fragLength = (ushort)Math.Min(message.BodyLength - bodyOffset, maxBodyPerFrag);
            var totalSize = hSize + fHeaderSize + fragLength;
            
            if (!_sendBuffer.TryReserve(totalSize, out var segment, out var span)) return false;
            
            message.WriteFragmentTo(span, fragId, index, totalCount, bodyOffset, fragLength);
            Send(segment);
        }
        
        return true;
    }

    private void Send(ArraySegment<byte> segment)
    {
        if (IsInClosingOrClosed || !TryAddState(SocketState.InSending)) return;
        if (!_udpArgsPool.TryRent(out var e))
        {
            OnSendEnded();
            return;
        }

        e.RemoteEndPoint = RemoteEndPoint;
        e.SetBuffer(segment.Array, segment.Offset, segment.Count);
        e.UserToken = this;
        e.Completed += OnSendCompleted;

        try
        {
            if (!_client.SendToAsync(e))
                OnSendCompleted(null, e);
        }
        catch (Exception ex)
        {
            HandleNetworkError(ex);
            OnSendCompleted(null, e);
        }
    }

    private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
    {
        e.Completed -= OnSendCompleted;

        try
        {
            if (e.SocketError != SocketError.Success)
            {
                LogError(new SocketException((int)e.SocketError));
            }
        
            _sendBuffer.Release(e.BytesTransferred);
        }
        finally
        {
            HandleArgsCleanup(e);
            OnSendEnded();
        }
    }

    private void OnSendEnded()
    {
        RemoveState(SocketState.InSending);
        if (IsInClosingOrClosed && IsIdle) OnClosed(_finalReason);
    }

    private void HandleArgsCleanup(SocketAsyncEventArgs e)
    {
        e.UserToken = null;
        e.RemoteEndPoint = null;
        e.SetBuffer(null, 0, 0);
        _udpArgsPool.Return(e);
    }

    public void SetMtu(ushort mtu)
    {
        // IP(20) + UDP(8) 헤더를 제외한 실제 데이터 가용 크기 계산
        _maxDataSize = (ushort)(mtu - 28);
    }

    public void UpdateRemoteEndPoint(IPEndPoint remoteEndPoint)
    {
        var current = RemoteEndPoint;
        if (current != null && current.Equals(remoteEndPoint)) return;
        Interlocked.Exchange(ref _remoteEndPoint, remoteEndPoint);
    }

    protected override void OnRelease()
    {
        _sendBuffer.Dispose();
        Assembler?.Dispose();
    }
}
