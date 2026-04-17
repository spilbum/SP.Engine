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
    public class PostList<T> : List<T>
    {
        public int Position { get; set; }
    }

    public class UdpSocket : IUnreliableSender
    {
        private const int AssemblerCleanupIntervalSec = 15;
        private readonly FragmentAssembler _assembler = new FragmentAssembler();
        private readonly PostList<ArraySegment<byte>> _itemsToSend = new PostList<ArraySegment<byte>>();
        private readonly byte[] _receiveBuffer;
        private readonly SocketAsyncEventArgs _receiveEventArgs = new SocketAsyncEventArgs();
        private readonly int _sendBufferSize;
        private readonly SwapQueue<ArraySegment<byte>> _sendQueue;
        private TickTimer _cleanupTimer;
        private int _fragSeq;
        private int _isSending;
        private ushort _maxDataSize = 512;
        private IPEndPoint _remoteEndPoint;
        private SocketAsyncEventArgs _sendEventArgs;
        private readonly SocketSendBuffer _sendBuffer = new SocketSendBuffer(1024 * 16);
        private Socket _socket;
        
        public UdpSocket(EngineConfig config)
        {
            _sendQueue = new SwapQueue<ArraySegment<byte>>(config.SendQueueSize);
            _sendBufferSize = config.SendBufferSize;
            _receiveBuffer = new byte[config.ReceiveBufferSize];
        }

        public IFragmentAssembler Assembler => _assembler;

        public bool IsRunning { get; private set; }
        
        public bool TrySend(UdpMessage message)
        {
            if (!IsRunning)
                return false;

            var items = new List<ArraySegment<byte>>();
            
            using (message)
            {
                if (message.Size <= _maxDataSize)
                {
                    if (!_sendBuffer.TryReserve(message.Size, out var segment, out var span)) return false;
                    message.WriteTo(span);
                    items.Add(segment);
                }
                else
                {
                    const int hSize = UdpHeader.ByteSize;
                    const int fHeaderSize = FragmentHeader.ByteSize;
                    var fragId = AllocateFragId();
                    var maxBodyPerFrag = _maxDataSize - hSize - fHeaderSize;
                    var totalCount = (byte)Math.Ceiling((double)message.BodyLength / maxBodyPerFrag);

                    for (byte index = 0; index < totalCount; index++)
                    {
                        var bodyOffset = index * maxBodyPerFrag;
                        var fragLen = (ushort)Math.Min(message.BodyLength - bodyOffset, maxBodyPerFrag);
                        var totalSize = hSize + fHeaderSize + fragLen;

                        if (!_sendBuffer.TryReserve(totalSize, out var segment, out var span)) 
                            return false;

                        message.WriteFragmentTo(span, fragId, index, totalCount, bodyOffset, fragLen);
                        items.Add(segment);
                    }
                }
            }

            if (!_sendQueue.TryEnqueue(items))
                return false;

            if (Interlocked.CompareExchange(ref _isSending, 1, 0) == 0)
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

        public void SetMaxFrameSize(ushort mtu)
        {
            _maxDataSize = (ushort)(mtu - 20 /* IP header size */ - 8 /* UDP header size*/);
        }

        public void Tick()
        {
            _cleanupTimer?.Tick();
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

                _cleanupTimer =
                    new TickTimer(_ => _assembler.Cleanup(TimeSpan.FromSeconds(AssemblerCleanupIntervalSec)), null, 0,
                        AssemblerCleanupIntervalSec / 2);

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

                _cleanupTimer?.Dispose();
                _cleanupTimer = null;
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
                _isSending = 0;
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
            catch (Exception e)
            {
                OnError(e);
            }
        }

        private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success || e.BytesTransferred <= 0)
            {
                _itemsToSend.Clear();
                _itemsToSend.Position = 0;
                Interlocked.Exchange(ref _isSending, 0);

                OnError(new SocketException((int)e.SocketError));
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
                Interlocked.Exchange(ref _isSending, 0);

                // 더블 체크
                _sendQueue.Exchange(_itemsToSend);
                if (_itemsToSend.Count == 0 || Interlocked.CompareExchange(ref _isSending, 1, 0) != 0)
                    return;
            }

            Send(_itemsToSend);
        }

        private void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success || e.BytesTransferred <= 0)
            {
                OnError(new SocketException((int)e.SocketError));
                return;
            }

            try
            {
                OnDataReceived(e.Buffer, e.Offset, e.BytesTransferred);
            }
            catch (Exception ex)
            {
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
    }
}
