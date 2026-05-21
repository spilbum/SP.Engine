using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Core;
using SP.Engine.Client.Configuration;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Client
{
    public class UdpNetworkSession : IUnreliableSender
    {
        private readonly SocketAsyncEventArgs _receiveEventArgs = new SocketAsyncEventArgs();
        private readonly PooledBuffer _receiveBuffer;
        private readonly int _sendBufferSize;
        private readonly ConcurrentQueue<PooledBuffer> _sendingQueue = new ConcurrentQueue<PooledBuffer>();
        private int _fragSeq = 1;
        private int _inSendingFlag;
        private ushort _maxFragmentSize = 512;
        private IPEndPoint _remoteEndPoint;
        private SocketAsyncEventArgs _sendEventArgs;
        private Socket _socket;
        
        public UdpNetworkSession(EngineConfig config)
        {
            _sendBufferSize = config.SendBufferSize;
            _receiveBuffer = new PooledBuffer(config.ReceiveBufferSize);
        }

        public bool IsRunning { get; private set; }

        public bool TrySend(UdpMessage message)
        {
            if (!IsRunning) return false;

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
                _sendingQueue.Enqueue(buffer);
            }

            if (Interlocked.CompareExchange(ref _inSendingFlag, 1, 0) == 0)
            {
                ExecuteSend();
            }
        
            return true;
        }
        
        private bool EnqueueFragments(UdpMessage message)
        {
            const int headerSize = UdpHeader.ByteSize;
            const int fragHeaderSize = FragmentHeader.ByteSize;

            var fragId = unchecked((uint)Interlocked.Increment(ref _fragSeq));
            var maxBodyPerFrag = _maxFragmentSize - headerSize - fragHeaderSize;
            var totalCount = (byte)Math.Ceiling((double)message.BodyLength / maxBodyPerFrag);

            for (byte index = 0; index < totalCount; index++)
            {
                var bodyOffset = index * maxBodyPerFrag;
                var fragLength = (ushort)Math.Min(message.BodyLength - bodyOffset, maxBodyPerFrag);
                var totalSize = headerSize + fragHeaderSize + fragLength;
            
                var buffer = new PooledBuffer(totalSize);
                message.WriteFragmentTo(buffer.Memory.Span, fragId, index, totalCount, bodyOffset, fragLength);
            
                _sendingQueue.Enqueue(buffer);
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
                _socket.SendBufferSize = _sendBufferSize;

                _receiveEventArgs.SetBuffer(_receiveBuffer.GetRawBuffer(), 0, _receiveBuffer.Length);
                _receiveEventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                _receiveEventArgs.Completed += OnReceiveCompleted;
                _socket.ReceiveFromAsync(_receiveEventArgs);

                IsRunning = true;
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
            if (!IsRunning)
                return;

            IsRunning = false;

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
            
            _receiveBuffer.Dispose();
            while (_sendingQueue.TryDequeue(out var buffer)) buffer.Dispose();
            OnClose();
        }

        private void ExecuteSend()
        {
            if (!_sendingQueue.TryPeek(out var buffer))
            {
                Interlocked.Exchange(ref _inSendingFlag, 0);
                return;
            }

            Send(buffer);
        }

        private void Send(PooledBuffer buffer)
        {
            if (_sendEventArgs == null)
            {
                _sendEventArgs = new SocketAsyncEventArgs();
                _sendEventArgs.Completed += OnSendCompleted;
            }

            try
            {
                _sendEventArgs.SetBuffer(buffer.Memory);
                _sendEventArgs.RemoteEndPoint = _remoteEndPoint;

                if (!_socket.SendAsync(_sendEventArgs))
                    OnSendCompleted(this, _sendEventArgs);
            }
            catch (Exception ex)
            {
                if (!IsIgnoreException(ex))
                    OnError(ex);
            }
        }
        
        private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (_sendingQueue.TryDequeue(out var completed))
            {
                completed.Dispose();
            }

            if (e.SocketError != SocketError.Success)
            {
                while (_sendingQueue.TryDequeue(out var buffer)) buffer.Dispose();
                Interlocked.Exchange(ref _inSendingFlag, 0);
                
                var ex = new SocketException((int)e.SocketError);
                if (!IsIgnoreException(ex))
                    OnError(ex);

                return;
            }

            ExecuteSend();
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
