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
        private readonly List<ArraySegment<byte>> _sendBufferList = new List<ArraySegment<byte>>();
        private readonly int _sendBufferSize;
        private readonly SwapBuffer<ArraySegment<byte>> _sendBuffer;
        private bool _isConnecting;
        private int _isSending;
        private byte[] _receiveBuffer;
        private SocketAsyncEventArgs _receiveEventArgs;
        private SocketAsyncEventArgs _sendEventArgs;
        private Socket _socket;
        private long _totalReceivedBytes;
        private long _totalSentBytes;

        public TcpNetworkSession(EngineConfig config)
        {
            _sendBufferSize = config.SendBufferSize;
            _receiveBufferSize = config.ReceiveBufferSize;
            _sendBuffer = new SwapBuffer<ArraySegment<byte>>(config.SendQueueSize);
        }

        public bool IsConnected { get; private set; }

        public bool TrySend(TcpMessage message)
        {
            if (!IsConnected)
                return false;

            var seg = message.ToArraySegment();
            if (!_sendBuffer.TryWrite(seg))
                return false;

            if (Interlocked.CompareExchange(ref _isSending, 1, 0) == 0)
                DequeueSend();

            return true;
        }

        public event EventHandler Opened;
        public event EventHandler Closed;
        public event EventHandler<ErrorEventArgs> Error;
        public event EventHandler<DataEventArgs> DataReceived;

        public TrafficInfo GetTrafficInfo()
        {
            return new TrafficInfo
            {
                SentBytes = Volatile.Read(ref _totalSentBytes),
                ReceivedBytes = Volatile.Read(ref _totalReceivedBytes)
            };
        }

        private void AddSentBytes(int bytes)
        {
            Interlocked.Add(ref _totalSentBytes, bytes);
        }

        private void AddReceivedBytes(int bytes)
        {
            Interlocked.Add(ref _totalReceivedBytes, bytes);
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

            var recvEventArgs = new SocketAsyncEventArgs();
            recvEventArgs.Completed += OnReceiveCompleted;
            GetSocket(recvEventArgs);
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

                try
                {
                    OnDataReceived(e.Buffer, e.Offset, e.BytesTransferred);
                }
                catch (Exception ex)
                {
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
                if (!IsIgnorableException(ex))
                    OnError(ex);
            }
        }

        private void DequeueSend()
        {
            _sendBuffer.Flush(_itemsToSend);

            if (_itemsToSend.Count == 0)
            {
                _isSending = 0;
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
            while (true)
            {
                if (e.SocketError != SocketError.Success || e.BytesTransferred == 0)
                {
                    if (EnsureSocketClosed()) 
                        OnClosed();

                    if (e.SocketError == SocketError.Success) 
                        return;
                    
                    var ex = new SocketException((int)e.SocketError);
                    if (!IsIgnorableException(ex)) 
                        OnError(ex);
                    
                    return;
                }

                AddSentBytes(e.BytesTransferred);

                if (e.BufferList is List<ArraySegment<byte>> list && list.Count > 0)
                {
                    ConsumeTransferred(list, e.BytesTransferred);

                    if (list.Count > 0)
                    {
                        if (_socket.SendAsync(e)) return;
                        continue;
                    }
                }

                e.BufferList = null;
                OnSendCompleted();
                break;
            }
        }

        private void OnSendCompleted()
        {
            _itemsToSend.Clear();
            _sendBuffer.Flush(_itemsToSend);

            if (_itemsToSend.Count == 0)
            {
                Interlocked.Exchange(ref _isSending, 0);

                _sendBuffer.Flush(_itemsToSend);
                if (_itemsToSend.Count == 0 || Interlocked.CompareExchange(ref _isSending, 1, 0) != 0)
                    return;
            }

            Send(_itemsToSend);
        }

        private static void ConsumeTransferred(List<ArraySegment<byte>> list, int bytesTransferred)
        {
            var remaining = bytesTransferred;
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
                    // 부분 소비: 현재 세그먼트만 슬라이스
                    if (seg.Array != null)
                        list[i] = new ArraySegment<byte>(seg.Array, seg.Offset + remaining, seg.Count - remaining);
                    break;
                }
            }

            if (i > 0)
                list.RemoveRange(0, i);
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
