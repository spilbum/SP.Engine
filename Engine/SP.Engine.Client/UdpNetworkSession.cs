using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Core.Buffers;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Client
{
    public class UdpNetworkSession : IUnreliableSender
    {
        private readonly SocketAsyncEventArgs _receiveEventArgs = new SocketAsyncEventArgs();
        private readonly SocketAsyncEventArgs _sendEventArgs;
        private byte[] _receiveBuffer;

        private int _inSending;
        private bool _isRunning;
        private ushort _maxFragmentSize = 512;
        private IPEndPoint _remoteEndPoint;
        private Socket _socket;
        private readonly NetPeerBase _netPeer;
        
        private readonly ConcurrentQueue<(BufferOwner Buffer, int Length)> _sendQueue = new ConcurrentQueue<(BufferOwner Buffer, int Length)>();
        
        public UdpNetworkSession(NetPeerBase netPeer)
        {
            _netPeer = netPeer;
            _sendEventArgs = new SocketAsyncEventArgs();
            _sendEventArgs.Completed += OnSendCompleted;
        }

        public bool TrySend(UdpMessage message)
        {
            if (!_isRunning) return false;
            
            const int MaxUdpQueueSize = 512;
            if (_sendQueue.Count >= MaxUdpQueueSize)
            {
                _netPeer.Logger.Warn("NetPeer {0} UDP Send Queue is full.", _netPeer.PeerId);
                return false;
            }

            if (message.TotalLength <= _maxFragmentSize)
            {
                // 단일 패킷 처리
                if (!message.TryGetBufferOwner(out var buffer, out var length))
                {
                    _netPeer.Logger.Warn("NetPeer {0} UDP TryGetBufferOwner failed.", _netPeer.PeerId);
                    return false;
                }
            
                _sendQueue.Enqueue((buffer, length));
            }
            else
            {
                // 패킷 파편화
                if (!message.TryGetFragments(_maxFragmentSize, out var fragments))
                {
                    _netPeer.Logger.Warn("NetPeer {0} UDP TryGetFragments failed.", _netPeer.PeerId);
                    return false;
                }

                foreach (var (buffer, length) in fragments)
                {
                    _sendQueue.Enqueue((buffer, length));
                }
            }

            if (Interlocked.CompareExchange(ref _inSending, 1, 0) == 0)
            {
                StartSend();
            }

            return true;
        }

        public event EventHandler<DataEventArgs> DataReceived;
        public event EventHandler<ErrorEventArgs> Error;
        public event EventHandler Closed;

        public void SetMaxFragmentSize(ushort mtu)
        {
            _maxFragmentSize = (ushort)(mtu - 20 /* IP header size */ - 8 /* UDP header size*/);
        }

        public bool Connect(string ip, int port)
        {
            try
            {
                _remoteEndPoint = new IPEndPoint(IPAddress.Parse(ip), port);
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _socket.Connect(_remoteEndPoint);
                _socket.SendBufferSize = _netPeer.Config.SendBufferSize;

                _receiveBuffer = new byte[_netPeer.Config.ReceiveBufferSize];

                _receiveEventArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
                _receiveEventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                _receiveEventArgs.Completed += OnReceiveCompleted;
                
                _socket.ReceiveFromAsync(_receiveEventArgs);

                _isRunning = true;
                return true;
            }
            catch (Exception e)
            {
                OnError(e);
                return false;
            }
        }

        public void Close()
        {
            if (!_isRunning)
                return;

            _isRunning = false;

            try
            {
                if (_socket != null)
                {
                    _socket.Close();
                    _socket.Dispose();
                    _socket = null;
                }
            }
            catch (Exception e)
            {
                OnError(e);
            }

            try
            {
                _receiveEventArgs?.Dispose();
                _sendEventArgs?.Dispose();
            }
            catch (Exception e)
            {
                OnError(e);
            }

            ClearSendQueue();
            
            OnClose();
        }

        private void ClearSendQueue()
        {
            while (_sendQueue.TryDequeue(out var item)) item.Buffer.Dispose();
        }
        
        private void StartSend()
        {
            while (true)
            {
                if (!_isRunning)
                {
                    Interlocked.Exchange(ref _inSending, 0);
                    return;
                }

                if (!_sendQueue.TryDequeue(out var item))
                {
                    Interlocked.Exchange(ref _inSending, 0);
                    return;
                }

                try
                {
                    _sendEventArgs.SetBuffer(item.Buffer.GetBuffer(), 0, item.Length);
                    _sendEventArgs.UserToken = item.Buffer;
                    _sendEventArgs.RemoteEndPoint = _remoteEndPoint;

                    if (!_socket.SendAsync(_sendEventArgs))
                    {
                        HandleSendResult(_sendEventArgs);
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref _inSending, 0);
                    if (!IsIgnoreException(ex)) OnError(ex);
                }

                break;
            }
        }

        private void HandleSendResult(SocketAsyncEventArgs e)
        {
            if (e.UserToken is BufferOwner bufferOwner)
            {
                bufferOwner.Dispose();
            }

            if (e.SocketError != SocketError.Success)
            {
                var ex = new SocketException((int)e.SocketError);
                if (!IsIgnoreException(ex)) OnError(ex);   
            }
            
            e.UserToken = null;
            e.SetBuffer(null, 0, 0);
        }

        private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
        {
            HandleSendResult(e);
        }

        private void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success || e.BytesTransferred <= 0)
            {
                var ex = new SocketException((int)e.SocketError);
                if (!IsIgnoreException(ex)) OnError(ex);
                return;
            }

            try
            {
                OnDataReceived(e.Buffer, e.Offset, e.BytesTransferred);
            }
            catch (Exception ex)
            {
                if (!IsIgnoreException(ex))
                    OnError(ex);
            }

            try
            {
                e.SetBuffer(0, _receiveBuffer.Length);
                if (e.RemoteEndPoint == null) e.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                if (!_socket.ReceiveFromAsync(e))
                    OnReceiveCompleted(this, e);
            }
            catch (Exception ex)
            {
                if (!IsIgnoreException(ex))
                    OnError(ex);
            }
        }

        private void OnError(Exception e)
        {
            Error?.Invoke(this, new ErrorEventArgs(e));
        }

        private void OnDataReceived(byte[] data, int offset, int length)
        {
            DataReceived?.Invoke(this, new DataEventArgs
            {
                Data = data,
                Offset = offset,
                Length = length
            });
        }

        private void OnClose()
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
        
        private static bool IsIgnoreException(Exception ex)
        {
            switch (ex)
            {
                case ObjectDisposedException _:
                case InvalidOperationException _:
                    return true;
                case SocketException socketException:
                    return IsIgnoreSocketError(socketException.SocketErrorCode);
                default:
                    return false;
            }
        }

        private static bool IsIgnoreSocketError(SocketError errorCode)
        {
            switch (errorCode)
            {
                case SocketError.Interrupted:       // 10004
                case SocketError.ConnectionAborted: // 10053
                case SocketError.ConnectionReset:   // 10054
                case SocketError.Shutdown:          // 10058
                case SocketError.TimedOut:          // 10060
                case SocketError.OperationAborted:  // 995
                    return true; 
                default:
                    return false;
            }
        }
    }
}
