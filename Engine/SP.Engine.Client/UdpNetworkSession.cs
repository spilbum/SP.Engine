using System;
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
    public class PostList<T> : List<T>
    {
        public int Position { get; set; }
    }

    public class UdpNetworkSession : IUnreliableSender
    {
        private readonly PostList<ArraySegment<byte>> _itemsToSend = new PostList<ArraySegment<byte>>();
        private readonly SocketAsyncEventArgs _receiveEventArgs = new SocketAsyncEventArgs();
        private readonly byte[] _receiveBuffer;
        private readonly int _sendBufferSize;
        private readonly SwapQueue<ArraySegment<byte>> _sendQueue;
        private int _fragSeq = 1;
        private int _sendingFlag;
        private ushort _maxFrameSize = 512;
        private IPEndPoint _remoteEndPoint;
        private SocketAsyncEventArgs _sendEventArgs;
        private readonly SessionSendBuffer _sendBuffer;
        private Socket _socket;
        
        public UdpNetworkSession(EngineConfig config)
        {
            _sendQueue = new SwapQueue<ArraySegment<byte>>(config.SendQueueSize);
            _sendBufferSize = config.SendBufferSize;
            _receiveBuffer = new byte[config.ReceiveBufferSize];
            _sendBuffer = new SessionSendBuffer(2048);
        }

        public bool IsRunning { get; private set; }
        public UdpFragmentAssembler Assembler { get; private set; }

        public void SetupAssembler(int assemblyTimeoutSec, int maxPendingMessageCount)
        {
            Assembler?.Dispose();
            Assembler = new UdpFragmentAssembler(assemblyTimeoutSec, maxPendingMessageCount);
        }

        public bool TrySend(UdpMessage message)
        {
            if (!IsRunning) return false;

            const int headerSize = UdpHeader.ByteSize;
            const int fragHeaderSize = UdpFragmentHeader.ByteSize;
            var maxBodyPerFrag = _maxFrameSize - headerSize - fragHeaderSize;

            if (message.Size <= _maxFrameSize)
            {
                if (!_sendBuffer.TryReserve(message.Size, out var segment)) return false;
                message.WriteTo(segment.AsSpan());
                return EnqueueSendingQueue(new List<ArraySegment<byte>> { segment });
            }

            // 조각화 계산
            var totalCount = (byte)((message.BodyLength + maxBodyPerFrag - 1) / maxBodyPerFrag);
            var fragId = AllocateFragId();
            var fragments = new List<ArraySegment<byte>>(totalCount);

            for (byte index = 0; index < totalCount; index++)
            {
                var bodyOffset = index * maxBodyPerFrag;
                var fragLen = (ushort)Math.Min(message.BodyLength - bodyOffset, maxBodyPerFrag);
                var totalSize = headerSize + fragHeaderSize + fragLen;

                if (!_sendBuffer.TryReserve(totalSize, out var segment))
                {
                    return false;
                }

                message.WriteFragmentTo(segment.AsSpan(), fragId, index, totalCount, bodyOffset, fragLen);
                fragments.Add(segment);
            }

            return EnqueueSendingQueue(fragments);
        }

        private bool EnqueueSendingQueue(List<ArraySegment<byte>> items)
        {
            if (!_sendQueue.TryEnqueue(items))
                return false;

            if (Interlocked.CompareExchange(ref _sendingFlag, 1, 0) == 0)
                DequeueSend();

            return true;
        }

        public event EventHandler<DataEventArgs> DataReceived;
        public event EventHandler<ErrorEventArgs> Error;
        public event EventHandler Closed;

        private uint AllocateFragId()
        {
            return unchecked((uint)Interlocked.Increment(ref _fragSeq));
        }

        public void SetupFrameSize(ushort mtu)
        {
            _maxFrameSize = (ushort)(mtu - 20 /* IP header size */ - 8 /* UDP header size*/);
        }

        public bool Connect(string ip, int port)
        {
            try
            {
                _remoteEndPoint = new IPEndPoint(IPAddress.Parse(ip), port);
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _socket.Connect(_remoteEndPoint);
                _socket.SendBufferSize = _sendBufferSize;

                _receiveEventArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
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

            _itemsToSend.Clear();
            _itemsToSend.Position = 0;
            
            _sendQueue.Dispose();
            _sendBuffer.Dispose();
            OnClose();
        }

        private void DequeueSend()
        {
            _sendQueue.Exchange(_itemsToSend);

            if (_itemsToSend.Count == 0)
            {
                _sendingFlag = 0;
                return;
            }

            Send(_itemsToSend);
        }

        private void Send(PostList<ArraySegment<byte>> items)
        {
            if (_sendEventArgs == null)
            {
                _sendEventArgs = new SocketAsyncEventArgs();
                _sendEventArgs.Completed += OnSendCompleted;
            }

            try
            {
                var segment = items[items.Position];
                _sendEventArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);
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
            if (e.SocketError != SocketError.Success || e.BytesTransferred <= 0)
            {
                _itemsToSend.Clear();
                _itemsToSend.Position = 0;
                Interlocked.Exchange(ref _sendingFlag, 0);

                var ex = new SocketException((int)e.SocketError);
                if (!IsIgnoreException(ex))
                    OnError(ex);
                return;
            }
            
            _sendBuffer.Release(e.BytesTransferred);
            
            // UDP는 부분 전송이 없으므로 다음 데이텀 단위로
            _itemsToSend.Position++;
            if (_itemsToSend.Position < _itemsToSend.Count)
            {
                Send(_itemsToSend);
                return;
            }

            OnSendCompleted();
        }

        private void OnSendCompleted()
        {
            _itemsToSend.Clear();
            _itemsToSend.Position = 0;
            _sendQueue.Exchange(_itemsToSend);

            if (_itemsToSend.Count == 0)
            {
                Interlocked.Exchange(ref _sendingFlag, 0);

                // 더블 체크
                _sendQueue.Exchange(_itemsToSend);
                if (_itemsToSend.Count == 0 || Interlocked.CompareExchange(ref _sendingFlag, 1, 0) != 0)
                    return;
            }

            Send(_itemsToSend);
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
