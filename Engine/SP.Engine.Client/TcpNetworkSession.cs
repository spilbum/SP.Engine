using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Common.Logging;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Client
{
    public class TcpNetworkSession
    {
        private Socket _socket;
        private SocketAsyncEventArgs _receiveEventArgs; 
        private SocketAsyncEventArgs _sendEventArgs; 
        private readonly ConcurrentBatchQueue<ArraySegment<byte>> _sendQueue;
        private readonly List<ArraySegment<byte>> _sendingItems = new List<ArraySegment<byte>>();
        private readonly List<ArraySegment<byte>> _sendBufferList = new List<ArraySegment<byte>>();
        private byte[] _receiveBuffer;
        private int _isSending;
        private bool _isConnecting;
        private readonly int _receiveBufferSize = 64 * 1024;
        private readonly int _sendQueueCapacity = 256;

        private long _totalSentBytes;
        private long _totalReceivedBytes;

        public event EventHandler Opened;
        public event EventHandler Closed;
        public event EventHandler<ErrorEventArgs> Error;
        public event EventHandler<DataEventArgs> DataReceived;

        public bool IsConnected { get; private set; }
        public EndPoint RemoteEndPoint { get; private set; }
        public ILogger Logger { get; private set; }

        public TcpNetworkSession(ILogger logger)
        {
            Logger = logger;
            _sendQueue = new ConcurrentBatchQueue<ArraySegment<byte>>(_sendQueueCapacity);
        }

        public (int totalSentBytes, int totalReceivedBytes) GetTraffic()
        {
            return ((int)Interlocked.Read(ref _totalSentBytes), (int)Interlocked.Read(ref _totalReceivedBytes));
        }

        private void AddSentBytes(int bytesCount)
        {
            Interlocked.Add(ref _totalSentBytes, bytesCount);
        }

        private void AddReceivedBytes(int bytesCount)
        {
            Interlocked.Add(ref _totalReceivedBytes, bytesCount);
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

            _isConnecting = false;

            if (null == e)
                e = new SocketAsyncEventArgs();
            e.Completed += OnReceiveCompleted;
            
            _socket = socket;
            
            try
            {
                _socket.ReceiveBufferSize = _receiveBufferSize;
            }
            catch (Exception exception)
            {
                Console.WriteLine(exception);
            }

            RemoteEndPoint = e.RemoteEndPoint;
            GetSocket(e);
        }

        private void GetSocket(SocketAsyncEventArgs e)
        {
            if (null == _receiveBuffer)
                _receiveBuffer = ArrayPool<byte>.Shared.Rent(_receiveBufferSize);

            e.SetBuffer(_receiveBuffer, 0, _receiveBufferSize);
            _receiveEventArgs = e;

            OnConnected();
            StartReceive(e);
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

            try
            {
                socket.Shutdown(SocketShutdown.Both);
            }
            catch
            {
                // ignored
            }
            finally
            {
                try
                {
                    socket.Close();
                }
                catch
                {
                    // ignored
                }
            }
            
            return isOnClosed;
        }

        private void StartReceive(SocketAsyncEventArgs e)
        {
            var socket = _socket;
            if (socket == null)
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

                AddReceivedBytes(e.BytesTransferred);
                OnDataReceived(e.Buffer, e.Offset, e.BytesTransferred);
                StartReceive(e);
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
            
            var enqueued = _sendQueue.Enqueue(message.Payload);
            if (!enqueued)
                return false;

            if (Interlocked.CompareExchange(ref _isSending, 1, 0) != 0)
                return true;
            
            DequeueSend();
            return true;
        }
        
        private void DequeueSend()
        {
            _sendQueue.DequeueAll(_sendingItems);
            if (_sendingItems.Count == 0)
            {
                _isSending = 0;
                return;
            }

            Send(_sendingItems);
        }

        private void Send(List<ArraySegment<byte>> items)
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

                var bytesCount = 0;
                foreach (var segment in items)
                {
                    _sendBufferList.Add(segment);
                    bytesCount += segment.Count;
                }

                _sendEventArgs.BufferList = _sendBufferList;
                
                if (!_socket.SendAsync(_sendEventArgs))
                    OnSendCompleted(null, _sendEventArgs);

                AddSentBytes(bytesCount);
            }
            catch (Exception ex)
            {
                OnError(new Exception(
                    $"Failed to send. Error: {ex.Message}, BufferList Count: {_sendEventArgs.BufferList?.Count ?? 0}",
                    ex));
                
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
            _sendingItems.Clear();
            
            _sendQueue.DequeueAll(_sendingItems);
            if (_sendingItems.Count == 0)
            {
                Interlocked.Exchange(ref _isSending, 0);
                return;
            }
            
            Send(_sendingItems);
        }

        private void OnConnected()
        {
            _isConnecting = false;
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
