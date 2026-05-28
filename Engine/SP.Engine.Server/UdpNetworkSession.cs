using System;
using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;
using Exception = System.Exception;

namespace SP.Engine.Server;

public class UdpNetworkSession : NetworkSessionBase, IUnreliableSender
{
    private readonly Channel<(IMemoryOwner<byte> Buffer, int Length)> _sendChannel;
    private readonly CancellationTokenSource _cts = new();
    private ushort _maxFragmentSize;
    
    public UdpNetworkSession(
        SessionBase session,
        Socket client,
        IPEndPoint remoteEndPoint)
        : base (SocketMode.Udp, client)
    {
        Session = session;
        RemoteEndPoint = remoteEndPoint;
        SetMaxFragmentSize(session.Config.Network.UdpMinMtu);

        _sendChannel = Channel.CreateBounded<(IMemoryOwner<byte>, int)>(
            new BoundedChannelOptions(session.Config.Network.UdpSendQueueCapacity)
            {
                SingleReader = true,
                SingleWriter = false,
                FullMode = BoundedChannelFullMode.DropWrite
            });
        
        Task.Run(ProcessSendLoopTask);
    }

    public bool TrySend(UdpMessage message)
    {
        if (IsClosed || IsInClosingOrClosed) return false;
        
        if (message.TotalLength > _maxFragmentSize)
        {
            // 패킷 파편화
            if (!message.TryExtractFragments(_maxFragmentSize, out var fragments))
                return false;
            
            for (var i = 0; i < fragments.Length; i++)
            {
                var item = fragments[i];
                if (_sendChannel.Writer.TryWrite(item)) continue;
                
                // 버퍼 정리 후 채널 종료 
                for (var j = i; j < fragments.Length; j++) 
                    fragments[j].Buffer.Dispose();
                Session.CloseUdpChannel(CloseReason.ServerBusy);
                return false;
            }
        }
        else
        {
            // 단일 패킷 전송
            if (!message.TryExtractBuffer(out var bufferOwner, out var length))
                return false;

            if (_sendChannel.Writer.TryWrite((bufferOwner, length))) return true;
            
            // 실패되면 채널 종료
            bufferOwner.Dispose();
            Session.CloseUdpChannel(CloseReason.ServerBusy);
            return false;
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
                while (reader.TryRead(out var item))
                {
                    var bufferOwner = item.Buffer;
                    var length = item.Length;
                    var ep = RemoteEndPoint;
                    var client = _client;
                    
                    if (IsClosed || client == null || ep == null)
                    {
                        bufferOwner.Dispose();
                        return;
                    }

                    if (!IncrementIo())
                    {
                        bufferOwner.Dispose();
                        return;
                    }

                    if (!TryAddState(SocketState.InSending))
                    {
                        DecrementIo();
                        bufferOwner.Dispose();
                        return;
                    }

                    try
                    {
                        await _client.SendToAsync(bufferOwner.Memory[..length], SocketFlags.None, ep, token);
                    }
                    catch (Exception ex)
                    {
                        LogError(ex);
                    }
                    finally
                    {
                        bufferOwner.Dispose();
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
        
        while (_sendChannel.Reader.TryRead(out var item))
            item.Buffer.Dispose();
        
        _cts.Dispose();
    }
    
    protected override bool ShouldSocketClosed() => false;
}
