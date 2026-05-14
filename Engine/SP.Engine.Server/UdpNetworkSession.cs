using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Server;

public class UdpNetworkSession : NetworkSessionBase, IUnreliableSender
{
    private SegmentQueue _sendingQueue;
    private readonly IObjectPool<SegmentQueue> _sendingQueuePool;
    private SocketAsyncEventArgs _sendArgs;
    private volatile int _inSendingFlag;
    private readonly SessionSendBuffer _sendBuffer;
    
    private uint _fragSeq;
    private ushort _maxFrameSize;
    
    public UdpFragmentAssembler Assembler { get; }

    public UdpNetworkSession(
        SessionBase session,
        Socket client,
        IPEndPoint remoteEndPoint,
        IObjectPool<SegmentQueue> sendingQueuePool)
        : base (SocketMode.Udp, client)
    {
        Session = session;
        RemoteEndPoint = remoteEndPoint;
        
        _sendingQueuePool = sendingQueuePool;
        if (!_sendingQueuePool.TryRent(out _sendingQueue))
        {
            throw new InvalidOperationException("Failed to rent segment queue for UDP session.");
        }
        
        _sendingQueue.StartEnqueue();
        _sendBuffer = new SessionSendBuffer(session.Config.Network.UdpMaxMtu + 1024);
        
        Assembler = new UdpFragmentAssembler(session.Config.Network.UdpCleanupPeriodSec, session.Config.Network.UdpMaxPendingMessageCount);
        
        SetupSendArgs(remoteEndPoint);
        SetupFrameSize(session.Config.Network.UdpMinMtu);
    }

    private void SetupSendArgs(IPEndPoint ep)
    {
        _sendArgs = new SocketAsyncEventArgs();
        _sendArgs.RemoteEndPoint = new IPEndPoint(ep.Address, ep.Port);
        _sendArgs.Completed += OnSendCompleted;
    }

    public bool TrySend(UdpMessage message)
    {
        if (IsClosed) return false;
        
        if (message.Size > _maxFrameSize)
        {
            // 패킷 파편화
            if (!EnqueueFragments(message)) return false;   
        }
        else
        {
            // 단일 패킷 전송
            if (!_sendBuffer.TryReserve(message.Size, out var segment)) return false;
            message.WriteTo(segment.AsSpan());
            if (!_sendingQueue.Enqueue(segment, _sendingQueue.TrackId)) return false;
        }

        if (Interlocked.CompareExchange(ref _inSendingFlag, 1, 0) == 0)
        {
            ExecuteSend(false);
        }
        
        return true;
    }

    private bool EnqueueFragments(UdpMessage message)
    {
        const int hSize = UdpHeader.ByteSize;
        const int fHeaderSize = UdpFragmentHeader.ByteSize;
        
        var fragId = Interlocked.Increment(ref _fragSeq);
        var maxBodyPerFrag = _maxFrameSize - hSize - fHeaderSize;
        var totalCount = (byte)Math.Ceiling((double)message.BodyLength / maxBodyPerFrag);

        for (byte index = 0; index < totalCount; index++)
        {
            var bodyOffset = index * maxBodyPerFrag;
            var fragLength = (ushort)Math.Min(message.BodyLength - bodyOffset, maxBodyPerFrag);
            var totalSize = hSize + fHeaderSize + fragLength;
            
            if (!_sendBuffer.TryReserve(totalSize, out var segment)) return false;
            
            message.WriteFragmentTo(segment.AsSpan(), fragId, index, totalCount, bodyOffset, fragLength);
            
            if (!_sendingQueue.Enqueue(segment, _sendingQueue.TrackId)) return false;
        }
        return true;
    }

    private void ExecuteSend(bool isContinuation)
    {
        if (!isContinuation)
        {
            if (!IncrementIo()) return;
            if (!TryAddState(SocketState.InSending))
            {
                Interlocked.Exchange(ref _inSendingFlag, 0);
                DecrementIo();
                return;
            }
        }

        while (true)
        {
            if (IsClosed || _client == null)
            {
                FinalizeSend();
                return;
            }

            try
            {
                if (_sendingQueue.Count == 0)
                {
                    Interlocked.Exchange(ref _inSendingFlag, 0);
                    if (_sendingQueue.Count > 0 && Interlocked.CompareExchange(ref _inSendingFlag, 1, 0) == 0)
                        continue;

                    FinalizeSend();
                    return;
                }

                // 하나씩 전송
                var segment = _sendingQueue[0];
                _sendArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);

                if (_client.SendToAsync(_sendArgs))
                    return;

                if (!HandleSendResult(_sendArgs))
                    return;
            }
            catch (Exception ex)
            {
                HandleNetworkError(ex);
                FinalizeSend();
                return;
            }
        }
    }

    private bool HandleSendResult(SocketAsyncEventArgs e)
    {
        if (e.SocketError != SocketError.Success)
        {
            LogError(new SocketException((int)e.SocketError));
            _sendingQueue.Clear();
            FinalizeSend();
            return false;
        }

        if (e.BytesTransferred <= 0) return true;
        
        _sendBuffer.Release(e.BytesTransferred);
        _sendingQueue.TrimSentBytes(e.BytesTransferred);
        return true;
    }

    private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
    {
        if (HandleSendResult(e))
        {
            ExecuteSend(true);
        }
    }

    private void FinalizeSend()
    {
        if (!RemoveState(SocketState.InSending)) return;
        Interlocked.Exchange(ref _inSendingFlag, 0);
        DecrementIo();
    }
    
    public void SetupFrameSize(ushort mtu)
    {
        // IP(20) + UDP(8) 헤더를 제외한 실제 데이터 가용 크기 계산
        _maxFrameSize = (ushort)(mtu - 28);
    }

    public void UpdateRemoteEndPoint(IPEndPoint remoteEndPoint)
    {
        var ep = RemoteEndPoint;
        if (ep != null && ep.Equals(remoteEndPoint)) return;
        Interlocked.Exchange(ref _remoteEndPoint, remoteEndPoint);
        _sendArgs.RemoteEndPoint = remoteEndPoint;
    }

    protected override void OnRelease()
    {
        var e = Interlocked.Exchange(ref _sendArgs, null);
        if (e != null)
        {
            e.Completed -= OnSendCompleted;
            e.Dispose();
        }
        
        var queue = Interlocked.Exchange(ref _sendingQueue, null);
        if (queue != null)
        {
            queue.Clear();
            _sendingQueuePool.Return(queue);
        }
        
        _sendBuffer.Dispose();
        Assembler?.Dispose();
    }
    
    protected override bool ShouldSocketClosed() => false;
}
