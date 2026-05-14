using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Core;
using SP.Engine.Client.Configuration;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Client
{
    public class TcpNetworkSession : IReliableSender
    {
        private readonly List<ArraySegment<byte>> _itemsToSend = new List<ArraySegment<byte>>();
        private readonly int _receiveBufferSize;
        private readonly int _sendBufferSize;
        private readonly SwapQueue<ArraySegment<byte>> _sendQueue;
        private bool _isConnecting;
        private int _inSendingFlag;
        private byte[] _receiveBuffer;
        private SocketAsyncEventArgs _receiveEventArgs;
        private SocketAsyncEventArgs _sendEventArgs;
        private Socket _socket;
        private readonly SessionSendBuffer _sendBuffer;

        public TcpNetworkSession(EngineConfig config)
        {
            _sendBufferSize = config.SendBufferSize;
            _receiveBufferSize = config.ReceiveBufferSize;
            _sendQueue = new SwapQueue<ArraySegment<byte>>(config.SendQueueSize);
            _sendBuffer = new SessionSendBuffer(4 * 1024);
        }

        public bool IsConnected { get; private set; }
        
        public bool TrySend(TcpMessage message)
        {
            if (!IsConnected)
                return false;

            if (!_sendBuffer.TryReserve(message.Size, out var segment)) 
                return false;

            message.WriteTo(segment.AsSpan());

            var enqueued = _sendQueue.TryEnqueue(segment);
            if (Interlocked.CompareExchange(ref _inSendingFlag, 1, 0) != 0)
                return enqueued;
            
            DequeueSend();
            return true;
        }

        public event EventHandler Opened;
        public event EventHandler Closed;
        public event EventHandler<ErrorEventArgs> Error;
        public event EventHandler<DataEventArgs> DataReceived;

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
                try
                {
                    e?.Dispose();
                }
                catch
                {
                    /* ignored */
                }

                _isConnecting = false;
                OnError(error);
                return;
            }

            if (socket == null)
            {
                try
                {
                    e?.Dispose();
                }
                catch
                {
                    /* ignored */
                }

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

                try
                {
                    e?.Dispose();
                }
                catch
                {
                    /* ignored */
                }

                OnError(new SocketException((int)se));
                return;
            }

            _socket = socket;
            _isConnecting = false;

            try
            {
                _socket.SendBufferSize = _sendBufferSize;
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

            try
            {
                e?.Dispose();
            }
            catch
            {
                /* ignored */
            }

            var receiveEventArgs = new SocketAsyncEventArgs();
            receiveEventArgs.Completed += OnReceiveCompleted;
            GetSocket(receiveEventArgs);
        }

        private void GetSocket(SocketAsyncEventArgs e)
        {
            if (null == _receiveBuffer)
                _receiveBuffer = ArrayPool<byte>.Shared.Rent(_receiveBufferSize);

            e.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
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
                _inSendingFlag = 0;
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

                if (!IsIgnoreException(ex))
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

                try
                {
                    OnDataReceived(e.Buffer, e.Offset, e.BytesTransferred);
                }
                catch (Exception ex)
                {
                    if (!IsIgnoreException(ex))
                        OnError(ex);
                }
                finally
                {
                    StartReceive(e);
                }
            }
            else
            {
                if (EnsureSocketClosed())
                    OnClosed();

                var ex = new SocketException((int)e.SocketError);
                if (!IsIgnoreException(ex))
                    OnError(ex);
            }
        }

        private void DequeueSend()
        {
            _sendQueue.Exchange(_itemsToSend);
            
            if (_itemsToSend.Count == 0)
            {
                _inSendingFlag = 0;
                return;
            }

            Send(_itemsToSend);
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
                if (items == null || items.Count == 0)
                    throw new ArgumentException("items cannot be null or empty.");

                if (items.Count > 1)
                {
                    _sendEventArgs.SetBuffer(null, 0, 0);
                    _sendEventArgs.BufferList = items;
                }
                else
                {
                    _sendEventArgs.BufferList = null;
                    var segment = items[0];
                    _sendEventArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);
                }

                if (!_socket.SendAsync(_sendEventArgs))
                    OnSendCompleted(null, _sendEventArgs);
            }
            catch (Exception ex)
            {
                if (EnsureSocketClosed() && !IsIgnoreException(ex))
                    OnError(ex);
            }
        }

        private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success || e.BytesTransferred == 0)
            {
                if (EnsureSocketClosed()) OnClosed();

                if (e.SocketError == SocketError.Success) return;

                var ex = new SocketException((int)e.SocketError);
                if (!IsIgnoreException(ex)) OnError(ex);

                return;
            }
            
            var transferred = e.BytesTransferred;
            var requested = 0;
            if (e.BufferList != null)
                foreach (var segment in e.BufferList) requested += segment.Count;
            else
                requested += e.Count;
            
            _sendBuffer.Release(transferred);

            if (transferred < requested)
            {
                var originals = 
                    e.BufferList ?? new List<ArraySegment<byte>> { new ArraySegment<byte>(e.Buffer, e.Offset, e.Count) };
                var remaining = CreateRemainingItems(originals, transferred);
        
                e.SetBuffer(null, 0, 0);
                e.BufferList = remaining;

                if (!_socket.SendAsync(e))
                    OnSendCompleted(sender, e);
                return;
            }

            e.BufferList = null;
            OnSendCompleted();
        }

        private static List<ArraySegment<byte>> CreateRemainingItems(IList<ArraySegment<byte>> originals, int bytesTransferred)
        {
            var remainingItems = new List<ArraySegment<byte>>();
            var skipBytes = bytesTransferred;

            foreach (var segment in originals)
            {
                if (segment.Array == null) continue;

                if (skipBytes >= segment.Count)
                {
                    skipBytes -= segment.Count;
                    continue;
                }

                if (skipBytes > 0)
                {
                    remainingItems.Add(new ArraySegment<byte>(
                        segment.Array, 
                        segment.Offset + skipBytes,
                        segment.Count - skipBytes));

                    skipBytes = 0;
                }
                else
                {
                    remainingItems.Add(segment);
                }
            }
            
            return remainingItems;
        }

        private void OnSendCompleted()
        {
            _itemsToSend.Clear();
            _sendQueue.Exchange(_itemsToSend);

            if (_itemsToSend.Count == 0)
            {
                Interlocked.Exchange(ref _inSendingFlag, 0);

                _sendQueue.Exchange(_itemsToSend);
                if (_itemsToSend.Count == 0 || Interlocked.CompareExchange(ref _inSendingFlag, 1, 0) != 0)
                    return;
            }

            Send(_itemsToSend);
        }

        private void OnConnected()
        {
            _isConnecting = false;
            IsConnected = true;
            Opened?.Invoke(this, EventArgs.Empty);
        }

        private void OnClosed()
        {
            if (!IsConnected) return;
            
            if (_receiveBuffer != null)
                ArrayPool<byte>.Shared.Return(_receiveBuffer);
            
            _sendQueue.Dispose();
            _sendBuffer.Dispose();
            _sendEventArgs?.Dispose();
            _receiveEventArgs?.Dispose();
            
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
