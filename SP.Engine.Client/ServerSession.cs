using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Engine.Runtime;

namespace SP.Engine.Client
{
    public class DataEventArgs : EventArgs
    {
        public byte[] Data { get; set; }
        public int Offset { get; set; }
        public int Length { get; set; }
    }

    public class ServerSession
    {
        private Socket _socket;
        private SocketAsyncEventArgs _socketEventArgs; // 수신 전용 SocketAsyncEventArgs
        private SocketAsyncEventArgs _socketEventArgsSend; // 송신 전용 SocketAsyncEventArgs

        private readonly List<ArraySegment<byte>> _dataToSend = new List<ArraySegment<byte>>();
        private readonly ConcurrentBatchQueue<ArraySegment<byte>> _sendQueue;

        private byte[] _receiveBuffer;
        private int _isSending;
        private bool _isConnecting;

        private int _receiveBufferSize = 64 * 1096;
        private int _sendQueueSize = 256;

        public event EventHandler Opened;
        public event EventHandler Closed;
        public event EventHandler<ErrorEventArgs> Error;
        public event EventHandler<DataEventArgs> DataReceived;

        public bool IsConnected { get; private set; }

        public ServerSession()
        {
            _sendQueue = new ConcurrentBatchQueue<ArraySegment<byte>>(_sendQueueSize);
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

        public int SendQueueSize
        {
            get => _sendQueueSize;
            set
            {
                if (IsConnected)
                    throw new InvalidOperationException("Cannot change send queue size while connected.");
                if (value <= 0)
                    throw new ArgumentOutOfRangeException(nameof(value), "Queue size must be greater than zero.");

                _sendQueueSize = value;
                _sendQueue.Resize(_sendQueueSize);
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

            GetSocket(e);
        }

        private void GetSocket(SocketAsyncEventArgs e)
        {
            if (null == _receiveBuffer)
                _receiveBuffer = new byte[ReceiveBufferSize];

            e.SetBuffer(_receiveBuffer, 0, ReceiveBufferSize);
            _socketEventArgs = e;

            OnConnected();
            StartReceive();
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

        private void StartReceive()
        {
            var socket = _socket;
            if (socket == null)
                return;

            var e = _socketEventArgs;
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
                StartReceive();
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

        public bool TrySend(ArraySegment<byte> segment)
        {
            if (_socket == null || !IsConnected)
                throw new InvalidOperationException("Socket is not connected.");

            if (segment.Array == null || segment.Count == 0)
                throw new ArgumentException("Invalid data segment.");

            var isEnqueued = _sendQueue.Enqueue(segment);
            if (Interlocked.CompareExchange(ref _isSending, 1, 0) != 0)
                return isEnqueued;

            DequeueSend();
            return true;
        }

        public bool TrySend(byte[] data, int offset, int length)
            => TrySend(new ArraySegment<byte>(data, offset, length));

        private void DequeueSend()
        {
            var items = _dataToSend;
            if (!_sendQueue.TryDequeue(ref items) || items.Count == 0)
            {
                _isSending = 0;
                return;
            }

            Send(items);
        }

        private void Send(List<ArraySegment<byte>> items)
        {
            if (_socketEventArgsSend == null)
            {
                _socketEventArgsSend = new SocketAsyncEventArgs();
                _socketEventArgsSend.Completed += OnSendCompleted;
            }

            try
            {
                if (items == null || items.Count == 0)
                    throw new ArgumentException("items cannot be null or empty.", nameof(items));

                if (items.Count > 1)
                {
                    _socketEventArgsSend.SetBuffer(null, 0, 0);
                    _socketEventArgsSend.BufferList = items;
                }
                else
                {
                    _socketEventArgsSend.BufferList = null;
                    var segment = items[0];
                    _socketEventArgsSend.SetBuffer(segment.Array, segment.Offset, segment.Count);
                }

                if (!_socket.SendAsync(_socketEventArgsSend))
                    OnSendCompleted(null, _socketEventArgsSend);
            }
            catch (Exception ex)
            {
                OnError(new Exception(
                    $"Buffer: {_socketEventArgsSend.Buffer}, BufferList: {_socketEventArgsSend.BufferList}"));
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
            var items = _dataToSend;
            items.Clear();

            if (!_sendQueue.TryDequeue(ref items) || items.Count == 0)
            {
                _isSending = 0;
                return;
            }

            Send(items);
        }

        private void OnConnected()
        {
            IsConnected = true;
            Opened?.Invoke(this, EventArgs.Empty);
        }

        private void OnClosed()
        {
            _receiveBuffer = null;

            _socketEventArgs?.Dispose();
            _socketEventArgs = null;

            _socketEventArgsSend?.Dispose();
            _socketEventArgsSend = null;

            IsConnected = false;
            Closed?.Invoke(this, EventArgs.Empty);
        }

        private void OnDataReceived(byte[] buffer, int offset, int length)
        {
            DataReceived?.Invoke(this, new DataEventArgs
            {
                Data = buffer,
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
