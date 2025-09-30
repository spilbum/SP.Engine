using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Common.Logging;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client
{
    public class TcpNetworkSession : ITcpSender
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
        private long _totalSentBytes;
        private long _totalReceivedBytes;
        private readonly int _sendBufferSize;
        private readonly int _receiveBufferSize;
        private readonly int _sendQueueSize;
        
        public event EventHandler Opened;
        public event EventHandler Closed;
        public event EventHandler<ErrorEventArgs> Error;
        public event EventHandler<DataEventArgs> DataReceived;

        public bool IsConnected { get; private set; }
        public EndPoint RemoteEndPoint { get; private set; }
        
        public TcpNetworkSession(
            int sendQueueSize = 512, 
            int sendBufferSize = 4 * 1024, 
            int receiveBufferSize = 64 * 1024)
        {
            _sendBufferSize = sendBufferSize;
            _receiveBufferSize = receiveBufferSize;
            _sendQueueSize = sendQueueSize;
            _sendQueue = new ConcurrentBatchQueue<ArraySegment<byte>>(sendQueueSize);
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
                try { e?.Dispose(); } catch { /* ignored */ }
                _isConnecting = false;
                OnError(error);
                return;
            }

            if (socket == null)
            {
                try { e?.Dispose(); } catch { /* ignored */ }
                _isConnecting = false;
                OnError(new SocketException((int)SocketError.ConnectionAborted));
                return;
            }

            if (!socket.Connected)
            {
                _isConnecting = false;
                SocketError se;
                try
                {
                    se = (SocketError)socket.GetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Error);
                }
                catch
                {
                    se = SocketError.ConnectionAborted;
                }
                try { e?.Dispose(); } catch { /* ignored */}
                OnError(new SocketException((int)se));
                return;
            }
            
            _socket = socket;
            _isConnecting = false;

            try
            {
                _socket.SendBufferSize = _sendBufferSize;
                _socket.ReceiveBufferSize = _receiveBufferSize;
                _socket.NoDelay = true;

                var vals = new byte[12];
                BitConverter.GetBytes((uint)1).CopyTo(vals, 0);
                BitConverter.GetBytes((uint)30_000).CopyTo(vals, 4);
                BitConverter.GetBytes((uint)2_000).CopyTo(vals, 8);

                try
                {
                    _socket.IOControl(IOControlCode.KeepAliveValues, vals, null);
                }
                catch
                {
                    _socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);
                }
            }
            catch
            {
                /* ignored */
            }
            
            try { e?.Dispose(); } catch { /* ignored */ }

            var recvEventArgs = new SocketAsyncEventArgs();
            recvEventArgs.Completed += OnReceiveCompleted;

            RemoteEndPoint = _socket.RemoteEndPoint;
            GetSocket(recvEventArgs);
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

        public bool TrySend(TcpMessage message)
        {
            if (!IsConnected)
                return false;
            
            if (!_sendQueue.Enqueue(message.Payload))
                return false;
            
            if (Interlocked.CompareExchange(ref _isSending, 1, 0) == 0)
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
                foreach (var seg in items)
                    _sendBufferList.Add(seg);

                _sendEventArgs.BufferList = _sendBufferList;
                
                if (!_socket.SendAsync(_sendEventArgs))
                    OnSendCompleted(null, _sendEventArgs);
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

                if (e.SocketError == SocketError.Success) return;
                var ex = new SocketException((int)e.SocketError);
                if (!IsIgnorableException(ex)) OnError(ex);
                return;
            }
            
            AddSentBytes(e.BytesTransferred);

            if (e.BufferList != null && e.BufferList.Count > 0)
            {
                var remaining = e.BytesTransferred;
                var list = (List<ArraySegment<byte>>)e.BufferList;

                var i = 0;
                while (i < list.Count && remaining > 0)
                {
                    var seg = list[i];
                    if (remaining >= seg.Count)
                    {
                        remaining -= seg.Count;
                        i++;
                    }
                    else
                    {
                        if (seg.Array != null)
                            list[i] = new ArraySegment<byte>(seg.Array, seg.Offset + remaining, seg.Count - remaining);
                        break;
                    }
                }

                if (i > 0)
                {
                    list.RemoveRange(0, i);
                }

                if (list.Count > 0)
                {
                    if (!_socket.SendAsync(e))
                        OnSendCompleted(null, e);
                    return;
                }
            }
            
            e.BufferList = null;
            
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
