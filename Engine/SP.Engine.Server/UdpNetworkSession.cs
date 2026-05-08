using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Engine.Runtime.Networking;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server;

public class UdpNetworkSession : BaseNetworkSession, IUnreliableSender
{
    private readonly SessionSendBuffer _sendBuffer;
    private uint _fragSeq;
    private ushort _maxFrameSize = 512;
    private SocketAsyncEventArgs _sendArgs;
    private readonly object _sendLock = new();

    public UdpFragmentAssembler Assembler { get; }

    public UdpNetworkSession(
        BaseSession session,
        Socket client,
        IPEndPoint remoteEndPoint,
        NetworkConfig config)
        : base (SocketMode.Udp, client)
    {
        Session = session;
        RemoteEndPoint = remoteEndPoint;
        Assembler = new UdpFragmentAssembler(config.UdpCleanupIntervalSec, config.UdpMaxPendingMessageCount);
        _sendBuffer = new SessionSendBuffer(config.SendBufferSize);
        SetupSendArgs(remoteEndPoint);
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

        // 파편화 여부 확인
        if (message.Size > _maxFrameSize) 
            return SendFragments(message);
        
        // 단일 패킷 전송
        if (!_sendBuffer.TryReserve(message.Size, out var segment)) return false;
        message.WriteTo(segment.AsSpan());
        return Send(segment);
    }

    private bool SendFragments(UdpMessage message)
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
            if (!Send(segment)) return false;
        }
        return true;
    }
    
    private bool Send(ArraySegment<byte> segment)
    {
        if (IsClosed) return false;

        lock (_sendLock)
        {
            var e = _sendArgs;
            if (e == null) return false;
        
            try
            {
                e.SetBuffer(segment.Array, segment.Offset, segment.Count);
                
                if (!_client.SendToAsync(e))
                {
                    OnSendCompleted(null, e);
                }
            
                return true;
            }
            catch (Exception ex)
            {
                HandleNetworkError(ex);
                return false;
            }   
        }
    }

    private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
    {
        try
        {
            if (e.SocketError != SocketError.Success)
            {
                LogError(new SocketException((int)e.SocketError));
            }
        
            _sendBuffer.Release(e.Count);
        }
        finally
        {
            e.SetBuffer(null, 0, 0);
        }
    }

    public void SetupMaxFrameSize(ushort mtu)
    {
        // IP(20) + UDP(8) 헤더를 제외한 실제 데이터 가용 크기 계산
        _maxFrameSize = (ushort)(mtu - 28);
    }

    public void UpdateRemoteEndPoint(IPEndPoint remoteEndPoint)
    {
        lock (_sendLock)
        {
            var current = RemoteEndPoint;
            if (current != null && current.Equals(remoteEndPoint)) return;
            
            Interlocked.Exchange(ref _remoteEndPoint, remoteEndPoint);

            if (_sendArgs != null)
            {
                _sendArgs.Completed -= OnSendCompleted;
                _sendArgs.Dispose();                
            }

            SetupSendArgs(remoteEndPoint);   
        }
    }

    protected override void OnRelease()
    {
        lock (_sendLock)
        {
            if (_sendArgs != null)
            {
                _sendArgs.Completed -= OnSendCompleted;
                _sendArgs.Dispose();
                _sendArgs = null;
            } 
        }
        
        _sendBuffer.Dispose();
        Assembler?.Dispose();
    }
    
    protected override bool ShouldSocketClosed() => false;
}
