using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SP.Core;
using SP.Engine.Runtime.Networking;
using Exception = System.Exception;

namespace SP.Engine.Server;

public class UdpNetworkSession : NetworkSessionBase, IUnreliableSender
{
    private readonly Channel<PooledBuffer> _sendChannel;
    private readonly CancellationTokenSource _cts = new();
    
    private uint _fragSeq;
    private ushort _maxFragmentSize;
    
    public UdpNetworkSession(
        Session session,
        Socket client,
        IPEndPoint remoteEndPoint)
        : base (SocketMode.Udp, client)
    {
        Session = session;
        RemoteEndPoint = remoteEndPoint;
        SetMaxFragmentSize(session.Config.Network.UdpMinMtu);

        var channelOptions = new BoundedChannelOptions(4096)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.DropWrite
        };
        
        _sendChannel = Channel.CreateBounded<PooledBuffer>(channelOptions);
        Task.Run(ProcessSendLoopTask);
    }

    public bool TrySend(UdpMessage message)
    {
        if (IsClosed || IsInClosingOrClosed) return false;
        
        if (message.Size > _maxFragmentSize)
        {
            // 패킷 파편화
            if (!EnqueueFragments(message)) return false;   
        }
        else
        {
            // 단일 패킷 전송
            var buffer = new PooledBuffer(message.Size);
            message.WriteTo(buffer.Memory.Span);

            if (!_sendChannel.Writer.TryWrite(buffer))
            {
                buffer.Dispose();
                return false;
            }
        }
        
        return true;
    }

    private bool EnqueueFragments(UdpMessage message)
    {
        const int headerSize = UdpHeader.ByteSize;
        const int fragHeaderSize = FragmentHeader.ByteSize;
        
        var fragId = Interlocked.Increment(ref _fragSeq);
        var maxBodyPerFrag = _maxFragmentSize - headerSize - fragHeaderSize;
        var totalCount = (byte)Math.Ceiling((double)message.BodyLength / maxBodyPerFrag);

        for (byte index = 0; index < totalCount; index++)
        {
            var bodyOffset = index * maxBodyPerFrag;
            var fragLength = (ushort)Math.Min(message.BodyLength - bodyOffset, maxBodyPerFrag);
            var totalSize = headerSize + fragHeaderSize + fragLength;
            
            var buffer = new PooledBuffer(totalSize);
            message.WriteFragmentTo(buffer.Memory.Span, fragId, index, totalCount, bodyOffset, fragLength);

            if (!_sendChannel.Writer.TryWrite(buffer))
            {
                buffer.Dispose();
                return false;
            }
        }
        
        return true;
    }

    private async Task ProcessSendLoopTask()
    {
        var reader = _sendChannel.Reader;
        var token = _cts.Token;

        try
        {
            while (await reader.WaitToReadAsync(token))
            {
                while (reader.TryRead(out var buffer))
                {
                    var ep = RemoteEndPoint;
                    var client = _client;
                    
                    if (IsClosed || client == null || ep == null)
                    {
                        buffer.Dispose();
                        return;
                    }

                    if (!IncrementIo())
                    {
                        buffer.Dispose();
                        return;
                    }

                    if (!TryAddState(SocketState.InSending))
                    {
                        DecrementIo();
                        buffer.Dispose();
                        return;
                    }

                    try
                    {
                        await _client.SendToAsync(buffer.Memory, SocketFlags.None, ep, token);
                    }
                    catch (Exception ex)
                    {
                        LogError(ex);
                    }
                    finally
                    {
                        buffer.Dispose();
                        RemoveState(SocketState.InSending);
                        DecrementIo();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {

        }
        catch (Exception ex)
        {
            LogError(ex);
        }
    }

    public void SetMaxFragmentSize(ushort size) => _maxFragmentSize = (ushort)(size - 28);

    public void UpdateRemoteEndPoint(IPEndPoint remoteEndPoint)
    {
        var ep = RemoteEndPoint;
        if (ep != null && ep.Equals(remoteEndPoint)) return;
        Interlocked.Exchange(ref _remoteEndPoint, remoteEndPoint);
    }

    protected override void OnRelease()
    {
        _sendChannel.Writer.Complete();
        _cts.Cancel();
        
        while (_sendChannel.Reader.TryRead(out var buffer)) buffer.Dispose();
        _cts.Dispose();
    }
    
    protected override bool ShouldSocketClosed() => false;
}
