using System;
using System.Buffers;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Client
{
    public class TcpNetworkSession : IReliableSender
    {
        private readonly ByteRingBuffer _sendRingBuffer;
        private int _inSending;
        private bool _isConnecting;
        private byte[] _receiveBuffer;
        private SocketAsyncEventArgs _receiveEventArgs;
        private SocketAsyncEventArgs _sendEventArgs;
        private Socket _socket;
        private readonly NetPeerBase _netPeer;

        public TcpNetworkSession(NetPeerBase netPeer)
        {
            _netPeer = netPeer;
            _sendRingBuffer = new ByteRingBuffer(1024 * 1024); // 1MB
        }

        public bool IsConnected { get; private set; }
        public event EventHandler Opened;
        public event EventHandler Closed;
        public event EventHandler<ErrorEventArgs> Error;
        public event EventHandler<DataEventArgs> DataReceived;

        public void Connect(EndPoint remoteEndPoint)
        {
            if (_isConnecting) throw new InvalidOperationException("Connection is already in progress.");
            if (IsConnected) throw new InvalidOperationException("Socket is already connected.");

            _isConnecting = true;
            remoteEndPoint.ResolveAndConnectAsync(ProcessConnect, null);
        }
        
        public bool TrySend(TcpMessage message)
        {
            try
            {
                if (!IsConnected) return false;

                if (!message.TryGetBuffer(out var memory))
                {
                    _netPeer.Logger.Warn("NetPeer {0} TCP TryGetBuffer failed. Id={1}, IsEmpty={2}",
                        _netPeer.PeerId, message.Id, message.IsEmpty);
                    return false;
                }
                
                using (var buffer = _sendRingBuffer.Lock())
                {
                    if (!buffer.TryWrite(memory.Span))
                    {
                        _netPeer.Logger.Warn("NetPeer {0} TCP TryWrite failed. RingBuffer: {1}/{2}",
                            _netPeer.PeerId, _sendRingBuffer.Size, _sendRingBuffer.Capacity);
                        return false;
                    }   
                }
            }
            catch (Exception ex)
            {
                if (!IsIgnoreException(ex)) OnError(ex);
                return false;
            }
            
            TryFlushSend();
            return true;
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
                _socket.SendBufferSize = _netPeer.Config.SendBufferSize;
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
                _receiveBuffer = ArrayPool<byte>.Shared.Rent(_netPeer.Config.ReceiveBufferSize);

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
                Interlocked.Exchange(ref _inSending, 0);
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

        private void TryFlushSend()
        {
            if (Interlocked.CompareExchange(ref _inSending, 1, 0) == 0)
                StartSend();
        }
        
        private void StartSend()
        {
            if (_sendEventArgs == null)
            {
                _sendEventArgs = new SocketAsyncEventArgs();
                _sendEventArgs.Completed += OnSendCompleted;
            }
            
            var socket = _socket;
            if (socket == null)
            {
                Interlocked.Exchange(ref _inSending, 0);
                return;
            }

            while (true)
            {
                ArraySegment<byte> segment;
                using (var buffer = _sendRingBuffer.Lock())
                {
                    segment = buffer.GetReadableSegment();
                }
                
                if (segment.Count == 0)
                {
                    Interlocked.Exchange(ref _inSending, 0);
                    return;
                }

                try
                {
                    _sendEventArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);
                    if (socket.SendAsync(_sendEventArgs))
                        return;

                    if (!ProcessSendResult(_sendEventArgs))
                        return;
                }
                catch (Exception ex)
                {
                    Interlocked.Exchange(ref _inSending, 0);
                    if (EnsureSocketClosed() && !IsIgnoreException(ex)) OnError(ex);
                }   
            }
        }
        
        private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (ProcessSendResult(e))
            {
                StartSend();
            }
        }

        private bool ProcessSendResult(SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success || e.BytesTransferred == 0)
            {
                Interlocked.Exchange(ref _inSending, 0);
                
                if (EnsureSocketClosed()) OnClosed();
                if (e.SocketError == SocketError.Success) return false;
                
                var ex = new SocketException((int)e.SocketError);
                if (!IsIgnoreException(ex)) OnError(ex);
                return false;
            }

            using (var buffer = _sendRingBuffer.Lock())
            {
                buffer.AdvanceRead(e.BytesTransferred);                
            }
            
            return true;
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
            
            _inSending = 0;
            using (var buffer = _sendRingBuffer.Lock()) buffer.Clear();
            
            if (_receiveBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(_receiveBuffer);
            }
            
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
