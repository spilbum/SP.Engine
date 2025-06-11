using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Message;

namespace SP.Engine.Client
{
    public class TcpNetworkSession
    {
        private Socket _socket;
        private SocketAsyncEventArgs _receiveEventArgs; 
        private SocketAsyncEventArgs _sendEventArgs; 
        private readonly ConcurrentBatchQueue<PooledSegment> _sendingQueue;
        private readonly List<PooledSegment> _sendingItems = new List<PooledSegment>();
        private readonly List<ArraySegment<byte>> _sendBufferList = new List<ArraySegment<byte>>();

        private byte[] _receiveBuffer;
        private int _isSending;
        private bool _isConnecting;

        private int _receiveBufferSize = 64 * 1096;
        private int _sendingQueueSize = 256;

        public event EventHandler Opened;
        public event EventHandler Closed;
        public event EventHandler<ErrorEventArgs> Error;
        public event EventHandler<DataEventArgs> DataReceived;

        public bool IsConnected { get; private set; }
        public IPEndPoint RemoteEndPoint { get; private set; }

        public TcpNetworkSession()
        {
            _sendingQueue = new ConcurrentBatchQueue<PooledSegment>(_sendingQueueSize);
        }

        public int ReceiveBufferSize
        {
            get => _receiveBufferSize;
            set
            {
                if (IsConnected)
                    throw new InvalidOperationException("Cannot change buffer size while connected.");
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "Buffer size must be greater than zero.");

                _receiveBufferSize = value;
            }
        }

        public int SendingQueueSize
        {
            get => _sendingQueueSize;
            set
            {
                if (IsConnected)
                    throw new InvalidOperationException("Cannot change send queue size while connected.");
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "Queue size must be greater than zero.");

                _sendingQueueSize = value;
                _sendingQueue.Resize(_sendingQueueSize);
            }
        }
        
        public void Connect(EndPoint remoteEndPoint)
        {
            if (_isConnecting)
                throw new InvalidOperationException("Connection is already in progress.");

            if (IsConnected)
                throw new InvalidOperationException("Socket is already connected.");

            _isConnecting = true;
            remoteEndPoint.ResolveAndConnectAsync(ProcessConnect, null);
        }

        private void ProcessConnect(Socket socket, object state, SocketAsyncEventArgs e, Exception error)
        {
            if (error != null)
            {
                e?.Dispose();
                _isConnecting = false;
                OnError(error);
                return;
            }

            if (socket == null)
            {
                e?.Dispose();
                _isConnecting = false;
                OnError(new SocketException((int)SocketError.ConnectionAborted));
                return;
            }

            if (null == e)
                e = new SocketAsyncEventArgs();
            e.Completed += OnReceiveCompleted;

            _socket = socket;

            try
            {
                _socket.ReceiveBufferSize = ReceiveBufferSize;
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
            }

            RemoteEndPoint = (IPEndPoint)e.RemoteEndPoint;
            GetSocket(e);
        }

        private void GetSocket(SocketAsyncEventArgs e)
        {
            if (null == _receiveBuffer)
                _receiveBuffer = ArrayPool<byte>.Shared.Rent(_receiveBufferSize);

            e.SetBuffer(_receiveBuffer, 0, ReceiveBufferSize);
            _receiveEventArgs = e;

            OnConnected();
            StartReceiving();
        }

        public void Close()
        {
            if (EnsureSocketClosed())
                OnClosed();
        }

        private bool EnsureSocketClosed(Socket previousSocket = null)
        {
            var socket = _socket;
            if (null == socket)
                return false;

            var isOnClosed = true;

            if (previousSocket != null && previousSocket != socket)
            {
                socket = previousSocket;
                isOnClosed = false;
            }
            else
            {
                _socket = null;
                _isSending = 0;
            }

            socket.SafeClose();
            return isOnClosed;
        }

        private void StartReceiving()
        {
            var socket = _socket;
            if (socket == null)
                return;

            var e = _receiveEventArgs;
            if (e == null)
                return;

            try
            {
                if (!socket.ReceiveAsync(e))
                    OnReceiveCompleted(null, e);
            }
            catch (Exception ex)
            {
                if (EnsureSocketClosed(socket))
                    OnClosed();

                if (!IsIgnorableException(ex))
                    OnError(ex);
            }
        }

        private void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError == SocketError.Success)
            {
                if (e.BytesTransferred == 0)
                {
                    // 종료 패킷 수신
                    if (EnsureSocketClosed())
                        OnClosed();
                    return;
                }

                OnDataReceived(e.Buffer, e.Offset, e.BytesTransferred);
                StartReceiving();
            }
            else
            {
                if (EnsureSocketClosed())
                    OnClosed();

                var ex = new SocketException((int)e.SocketError);
                if (!IsIgnorableException(ex))
                    OnError(ex);
            }
        }

        public bool Send(TcpMessage message)
        {
            if (!IsConnected)
                return false;
            
            var pooled = PooledSegment.FromMessage(message);
            var enqueued = _sendingQueue.Enqueue(pooled);
            if (!enqueued)
                return false;

            if (Interlocked.CompareExchange(ref _isSending, 1, 0) != 0)
                return true;
            
            DequeueSend();
            return true;
        }
        
        private void DequeueSend()
        {
            if (!_sendingQueue.TryDequeue(_sendingItems))
            {
                _isSending = 0;
                return;
            }

            Send(_sendingItems);
        }

        private void Send(List<PooledSegment> items)
        {
            if (_sendEventArgs == null)
            {
                _sendEventArgs = new SocketAsyncEventArgs();
                _sendEventArgs.Completed += OnSendCompleted;
            }

            try
            {
                _sendEventArgs.SetBuffer(null, 0, 0);
                
                _sendBufferList.Clear();
                foreach (var pooled in items)
                    _sendBufferList.Add(pooled.Segment);
                
                _sendEventArgs.BufferList = _sendBufferList;
                
                if (!_socket.SendAsync(_sendEventArgs))
                    OnSendCompleted(null, _sendEventArgs);
            }
            catch (Exception ex)
            {
                OnError(new Exception(
                    $"Failed to send. Error: {ex.Message}, BufferList Count: {_sendEventArgs.BufferList?.Count ?? 0}",
                    ex));
                
                foreach (var pooled in items)
                    pooled.Dispose();
                
                if (EnsureSocketClosed() && !IsIgnorableException(ex))
                    OnError(ex);
            }
        }

        private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success || e.BytesTransferred == 0)
            {
                if (EnsureSocketClosed())
                    OnClosed();

                var ex = new SocketException((int)e.SocketError);
                if (e.SocketError != SocketError.Success && !IsIgnorableException(ex))
                    OnError(ex);

                return;
            }

            OnSendCompleted();
        }

        private void OnSendCompleted()
        {
            foreach (var pooled in _sendingItems)
                pooled.Dispose();
            
            _sendingItems.Clear();
            
            if (!_sendingQueue.TryDequeue(_sendingItems))
            {
                _isSending = 0;
                return;
            }
            
            Send(_sendingItems);
        }

        private void OnConnected()
        {
            IsConnected = true;
            Opened?.Invoke(this, EventArgs.Empty);
        }

        private void OnClosed()
        {
            if (_receiveBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(_receiveBuffer);
                _receiveBuffer = null;
            }

            _receiveEventArgs?.Dispose();
            _receiveEventArgs = null;
            _sendEventArgs?.Dispose();
            _sendEventArgs = null;
            
            IsConnected = false;
            Closed?.Invoke(this, EventArgs.Empty);
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

        private void OnError(Exception ex)
        {
            Error?.Invoke(this, new ErrorEventArgs(ex));
        }

        private static bool IsIgnorableException(Exception ex)
        {
            switch (ex)
            {
                case ObjectDisposedException _:
                case InvalidOperationException _:
                    return true;
                case SocketException socketException:
                    return IsIgnorableSocketError(socketException.SocketErrorCode);
                default:
                    return false;
            }
        }

        private static bool IsIgnorableSocketError(SocketError error)
        {
            return error == SocketError.Shutdown || error == SocketError.ConnectionAborted ||
                   error == SocketError.ConnectionReset || error == SocketError.OperationAborted;
        }
    }
}
