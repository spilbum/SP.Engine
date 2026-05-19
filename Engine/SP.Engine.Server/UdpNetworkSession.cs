using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Core;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Server;

public class UdpNetworkSession : NetworkSessionBase, IUnreliableSender
{
    private SocketAsyncEventArgs _sendEventArgs;
    private volatile int _inSendingFlag;

    private readonly ConcurrentQueue<PooledBuffer> _sendingQueue = new();
    
    private uint _fragSeq;
    private ushort _maxFrameSize;
    
    public UdpNetworkSession(
        Session session,
        Socket client,
        IPEndPoint remoteEndPoint)
        : base (SocketMode.Udp, client)
    {
        Session = session;
        RemoteEndPoint = remoteEndPoint;

        SetupSendArgs(remoteEndPoint);
        SetupFrameSize(session.Config.Network.UdpMinMtu);
    }

    private void SetupSendArgs(IPEndPoint ep)
    {
        _sendEventArgs = new SocketAsyncEventArgs();
        _sendEventArgs.RemoteEndPoint = new IPEndPoint(ep.Address, ep.Port);
        _sendEventArgs.Completed += OnSendCompleted;
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
            var buffer = new PooledBuffer(message.Size);
            message.WriteTo(buffer.Memory.Span);
            _sendingQueue.Enqueue(buffer);
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
            
            var buffer = new PooledBuffer(totalSize);
            message.WriteFragmentTo(buffer.Memory.Span, fragId, index, totalCount, bodyOffset, fragLength);
            
            _sendingQueue.Enqueue(buffer);
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
                if (!_sendingQueue.TryPeek(out var buffer))
                {
                    Interlocked.Exchange(ref _inSendingFlag, 0);
                    if (!_sendingQueue.IsEmpty && Interlocked.CompareExchange(ref _inSendingFlag, 1, 0) == 0)
                        continue;
                    
                    FinalizeSend();
                    return;
                }
                
                _sendEventArgs.SetBuffer(buffer.Memory);

                if (_client.SendToAsync(_sendEventArgs))
                    return;

                if (!HandleSendResult(_sendEventArgs))
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
        if (_sendingQueue.TryDequeue(out var completedBuffer))
        {
            completedBuffer.Dispose();
        }
        
        if (e.SocketError != SocketError.Success)
        {
            LogError(new SocketException((int)e.SocketError));
            ClearQueueAndFinalize();
            return false;
        }

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

    private void ClearQueueAndFinalize()
    {
        while (_sendingQueue.TryDequeue(out var buffer))
        {
            buffer.Dispose();
        }
        FinalizeSend();
    }
    
    public void SetupFrameSize(ushort mtu) => _maxFrameSize = (ushort)(mtu - 28);

    public void UpdateRemoteEndPoint(IPEndPoint remoteEndPoint)
    {
        var ep = RemoteEndPoint;
        if (ep != null && ep.Equals(remoteEndPoint)) return;
        Interlocked.Exchange(ref _remoteEndPoint, remoteEndPoint);
        _sendEventArgs.RemoteEndPoint = remoteEndPoint;
    }

    protected override void OnRelease()
    {
        var e = Interlocked.Exchange(ref _sendEventArgs, null);
        if (e != null)
        {
            e.Completed -= OnSendCompleted;
            e.Dispose();
        }
        
        ClearQueueAndFinalize();
    }
    
    protected override bool ShouldSocketClosed() => false;
}
