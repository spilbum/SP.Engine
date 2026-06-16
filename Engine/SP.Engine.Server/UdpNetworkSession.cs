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
    private sealed record UdpRouteContext(Socket Socket, IPEndPoint EndPoint);
    
    private ushort _maxFragmentSize;
    private int _inSending; // 0: Idle, 1: Sending
    private SocketAsyncEventArgs _sendEventArgs;
    private volatile UdpRouteContext _routeContext;
    private uint _nextFragId;

    private readonly ConcurrentQueue<(BufferOwner Buffer, int Length)> _sendQueue = new();

    public UdpNetworkSession(SessionBase session, Socket client, IPEndPoint remoteEndPoint)
        : base (SocketMode.Udp, client)
    {
        Session = session;
        _routeContext = new UdpRouteContext(client, remoteEndPoint);

        _sendEventArgs = new SocketAsyncEventArgs();
        _sendEventArgs.Completed += OnSendCompleted;
        
        SetMaxFragmentSize(session.Config.Network.UdpMinMtu);
    }

    public bool TrySend(UdpMessage message)
    {
        if (IsInClosingOrClosed || message.IsEmpty) return false;

        const int MaxUdpQueueSize = 512;
        if (_sendQueue.Count >= MaxUdpQueueSize)
        {
            Session.Logger.Warn("Session {0} UDP Send Queue is full.", Session.SessionId);
            return false;
        }
        
        if (message.TotalLength <= _maxFragmentSize)
        {
            // 단일 패킷 처리
            if (!message.TryGetBufferOwner(out var buffer, out var length)) return false;
            _sendQueue.Enqueue((buffer, length));
        }
        else
        {
            // 패킷 파편화
            var fragId = Interlocked.Increment(ref _nextFragId);
            if (!message.TryGetFragments(fragId, _maxFragmentSize, out var fragments)) return false;
            
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
        int syncCount = 0;
        const int MaxSyncCompletions = 50;
        
        while (true)
        {
            if (IsInClosingOrClosed)
            {
                ClearSendQueue();
                Interlocked.Exchange(ref _inSending, 0);
                return;
            }

            var e = Volatile.Read(ref _sendEventArgs);
            if (e == null)
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

            var route = _routeContext;
            if (route.Socket == null)
            {
                item.Buffer.Dispose();
                ProcessSendCompleted(e);
                Interlocked.Exchange(ref _inSending, 0);
                return;
            }
            
            e.SetBuffer(item.Buffer.GetBuffer(), 0, item.Length);
            e.UserToken = item.Buffer;
            e.RemoteEndPoint = route.EndPoint;

            bool pending;
            try
            {
                pending = route.Socket.SendToAsync(e);
            }
            catch (Exception ex)
            {
                LogError(ex);
                ProcessSendCompleted(e);
                Interlocked.Exchange(ref _inSending, 0);
                return;
            }

            if (pending) return;
            
            ProcessSendCompleted(e);
            
            syncCount++;
            if (syncCount >= MaxSyncCompletions)
            {
                Session.AsyncRun(StartSend);
                return;
            }
        }
    }

    private void ProcessSendCompleted(SocketAsyncEventArgs e)
    {
        if (e.UserToken is BufferOwner bufferOwner)
            bufferOwner.Dispose();

        e.UserToken = null;
        e.SetBuffer(null, 0, 0);  
        
        DecrementIo();
    }

    private void ProcessSend(SocketAsyncEventArgs e)
    {
        ProcessSendCompleted(e); 
        StartSend();
    }

    private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
    {
        ProcessSend(e);
    }

    private void ClearSendQueue()
    {
        while (_sendQueue.TryDequeue(out var item)) 
            item.Buffer.Dispose();
    }

    public void SetMaxFragmentSize(ushort size) => _maxFragmentSize = (ushort)(size - 28);

    public void UpdateContext(Socket socket, IPEndPoint remoteEndPoint)
    {
        var current = _routeContext;
        if (ReferenceEquals(current.Socket, socket) && current.EndPoint.Equals(remoteEndPoint))
            return;
        
        Interlocked.Exchange(ref _routeContext, new UdpRouteContext(socket, remoteEndPoint));

        Interlocked.Exchange(ref _client, socket);
        Volatile.Write(ref _remoteEndPoint, remoteEndPoint);
    }

    protected override bool ShouldSocketClosed() => false;

    protected override void OnRelease()
    {
        base.OnRelease();
        
        ClearSendQueue();
        
        var e = Interlocked.Exchange(ref _sendEventArgs, null);
        if (e != null)
        {
            e.Completed -= OnSendCompleted;
            e.Dispose();
        }
    }
}
