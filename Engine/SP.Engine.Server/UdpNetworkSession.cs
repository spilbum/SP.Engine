using System;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Core.Buffers;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Server;

public class UdpNetworkSession : NetworkSessionBase, IUnreliableSender
{
    private ushort _maxFragmentSize;
    private int _inSending; // 0: Idle, 1: Sending
    private readonly SocketAsyncEventArgs _sendEventArgs;

    private readonly ConcurrentQueue<(BufferOwner Buffer, int Length)> _sendQueue = new();

    public UdpNetworkSession(
        SessionBase session,
        Socket client,
        IPEndPoint remoteEndPoint)
        : base (SocketMode.Udp, client)
    {
        Session = session;
        RemoteEndPoint = remoteEndPoint;

        _sendEventArgs = new SocketAsyncEventArgs();
        _sendEventArgs.RemoteEndPoint = remoteEndPoint;
        _sendEventArgs.Completed += OnSendCompleted;
        
        SetMaxFragmentSize(session.Config.Network.UdpMinMtu);
    }

    public bool TrySend(UdpMessage message)
    {
        if (IsClosed || IsInClosingOrClosed || message.IsEmpty) return false;

        const int MaxUdpQueueSize = 512;
        if (_sendQueue.Count >= MaxUdpQueueSize)
        {
            Session.Logger.Warn("Session {0} UDP Send Queue is full.", Session.SessionId);
            return false;
        }
        
        if (message.TotalLength <= _maxFragmentSize)
        {
            // 단일 패킷 처리
            if (!message.TryGetBufferOwner(out var buffer, out var length))
            {
                Session.Logger.Warn("Session {0} UDP TryGetBufferOwner failed.", Session.SessionId);
                return false;
            }
            
            _sendQueue.Enqueue((buffer, length));
        }
        else
        {
            // 패킷 파편화
            if (!message.TryGetFragments(_maxFragmentSize, out var fragments))
            {
                Session.Logger.Warn("Session {0} UDP TryGetFragments failed.", Session.SessionId);
                return false;
            }

            foreach (var (buffer, length) in fragments)
            {
                _sendQueue.Enqueue((buffer, length));
            }
        }

        TryFlushSend();
        return true;
    }

    private void TryFlushSend()
    {
        if (Interlocked.CompareExchange(ref _inSending, 1, 0) == 0)
        {
            Session.AsyncRun(StartSend);
        }
    }

    private void StartSend()
    {
        while (true)
        {
            if (IsClosed)
            {
                ClearSendQueue();
                Interlocked.Exchange(ref _inSending, 0);
                return;
            }

            if (!_sendQueue.TryDequeue(out var item))
            {
                Interlocked.Exchange(ref _inSending, 0);
                return;
            }

            if (!IncrementIo())
            {
                item.Buffer.Dispose();
                Interlocked.Exchange(ref _inSending, 0);
                return;
            }

            if (!TryAddState(SocketState.InSending))
            {
                DecrementIo();
                item.Buffer.Dispose();
                Interlocked.Exchange(ref _inSending, 0);
                return;
            }
            
            _sendEventArgs.SetBuffer(item.Buffer.GetBuffer(), 0, item.Length);
            _sendEventArgs.UserToken = item.Buffer;
            _sendEventArgs.RemoteEndPoint = RemoteEndPoint;
            
            var socket = Volatile.Read(ref _client);
            if (socket == null)
            {
                HandleSendResult(_sendEventArgs);
                continue;
            }

            try
            {
                if (!socket.SendToAsync(_sendEventArgs))
                {
                    HandleSendResult(_sendEventArgs);
                    continue;
                }
            }
            catch (Exception ex)
            {
                LogError(ex);
                HandleSendResult(_sendEventArgs);
                Interlocked.Exchange(ref _inSending, 0);
            }

            break;
        }
    }

    private void HandleSendResult(SocketAsyncEventArgs e)
    {
        RemoveState(SocketState.InSending);
        DecrementIo();
        
        if (e.UserToken is BufferOwner bufferOwner)
        {
            bufferOwner.Dispose();
        }
        
        e.UserToken = null;
        e.SetBuffer(null, 0, 0);
    }

    private void ProcessSend(SocketAsyncEventArgs e)
    {
        HandleSendResult(e);
        StartSend();
    }

    private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
    {
        ProcessSend(e);
    }

    private void ClearSendQueue()
    {
        while (_sendQueue.TryDequeue(out var item)) item.Buffer.Dispose();
    }

    public void SetMaxFragmentSize(ushort size) => _maxFragmentSize = (ushort)(size - 28);

    public void UpdateContext(Socket socket, IPEndPoint remoteEndPoint)
    {
        if (!ReferenceEquals(_client, socket))
        {
            Interlocked.Exchange(ref _client, socket);
        }
        
        var ep = RemoteEndPoint;
        if (ep != null && ep.Equals(remoteEndPoint)) return;
        
        Interlocked.Exchange(ref _remoteEndPoint, remoteEndPoint);
    }

    protected override bool ShouldSocketClosed() => false;

    protected override void OnRelease()
    {
        base.OnRelease();
        
        ClearSendQueue();
        _sendEventArgs.Dispose();
    }
}
