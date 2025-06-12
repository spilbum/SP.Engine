using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Common.Fiber;
using SP.Engine.Protocol;
using SP.Engine.Runtime.Message;

namespace SP.Engine.Client
{
    public class PostList<T> : List<T>
    {
        public int Position { get; set; }
    }
    
    public class UdpSocket
    {
        private Socket _socket;
        private IPEndPoint _remoteEndPoint;
        private readonly SocketAsyncEventArgs _receiveEventArgs = new SocketAsyncEventArgs();
        private readonly SocketAsyncEventArgs _sendEventArgs = new SocketAsyncEventArgs();
        private readonly PostList<PooledSegment> _sendingItems = new PostList<PooledSegment>();
        private ConcurrentBatchQueue<PooledSegment> _sendingQueue;
        private byte[] _receiveBuffer;
        private int _isSending;
        private NetPeer _netPeer;
        private DateTime? _nextKeepAliveTime;
        private ushort _mtu;
        private long _nextFragmentId;
        
        public UdpFragmentAssembler Assembler { get; } = new UdpFragmentAssembler();

        public event EventHandler<DataEventArgs> DataReceived;
        public event EventHandler<ErrorEventArgs> Error;

        public bool Connect(NetPeer netPeer, string ip, int port, ushort mtu)
        {
            try
            {
                _netPeer = netPeer;
                _mtu = mtu;
                _remoteEndPoint = new IPEndPoint(IPAddress.Parse(ip), port);
                _sendingQueue = new ConcurrentBatchQueue<PooledSegment>(300);
                _receiveBuffer = new byte[ushort.MaxValue];
            
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _socket.Connect(_remoteEndPoint);

                _receiveEventArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
                _receiveEventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                _receiveEventArgs.Completed += OnReceiveCompleted;
                _socket.ReceiveFromAsync(_receiveEventArgs);
            
                _sendEventArgs.Completed += OnSendCompleted;
                return true;
            }
            catch (Exception e)
            {
                netPeer.Logger.Error(e);
                return false;
            }
        }

        private void CheckKeepAlive()
        {
            if (_nextKeepAliveTime != null && DateTime.UtcNow < _nextKeepAliveTime)
                return;
            
            _nextKeepAliveTime = DateTime.UtcNow.AddSeconds(5);
            var keepAlive = new EngineProtocolData.C2S.UdpKeepAlive();
            var message = new UdpMessage();
            message.SetPeerId(_netPeer.PeerId);
            message.Pack(keepAlive, null, null);
            Send(message);
        }

        public void Tick()
        {
            //CheckKeepAlive();
        }
        
        public void Close()
        {
            try
            {
                _socket?.Close();
                _socket?.Dispose();
                _socket = null;
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
            
            _sendingItems.Clear();
            _sendingItems.Position = 0;
        }
        
        public bool Send(UdpMessage message)
        {
            var items = ToSegments(message);
            if (!_sendingQueue.Enqueue(items))
                return false;
            
            DequeueSend();
            return true;
        }
        
        private List<PooledSegment> ToSegments(UdpMessage message)
        {
            var segments = new List<PooledSegment>();
            if (message.Length <= _mtu)
            {
                segments.Add(PooledSegment.FromMessage(message));
                return segments;
            }

            var fragmentId = (uint)Interlocked.Increment(ref _nextFragmentId);
            segments.AddRange(message.ToSplit(_mtu, fragmentId).Select(PooledSegment.FromFragment));
            return segments;
        }

        private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.SocketError != SocketError.Success || e.BytesTransferred <= 0)
            {
                OnError(new SocketException((int)e.SocketError));
                return;
            }

            var pooled = _sendingItems[_sendingItems.Position];
            pooled.Dispose();

            _sendingItems.Position++;
            if (_sendingItems.Position < _sendingItems.Count)
            {
                Send(_sendingItems);
                return;
            }
            
            OnSendCompleted();
        }

        private void OnSendCompleted()
        {
            _sendingItems.Clear();
            _sendingItems.Position = 0;
            
            if (!_sendingQueue.TryDequeue(_sendingItems))
            {
                Interlocked.Exchange(ref _isSending, 0);
                return;
            }
            
            Send(_sendingItems);
        }

        private void Send(PostList<PooledSegment> items)
        {
            if (items.Position >= items.Count)
            {
                Interlocked.Exchange(ref _isSending, 0);    
                DequeueSend();
                return;
            }
            
            var segment = items[items.Position].Segment;
            _sendEventArgs.SetBuffer(segment.Array, segment.Offset, segment.Count);
            _sendEventArgs.RemoteEndPoint = _remoteEndPoint;

            if (!_socket.SendAsync(_sendEventArgs))
                OnSendCompleted(this, _sendEventArgs);
        }

        private void DequeueSend()
        {
            if (Interlocked.CompareExchange(ref _isSending, 1, 0) != 0)
                return;

            if (!_sendingQueue.TryDequeue(_sendingItems))
            {
                _isSending = 0;
                return;
            }

            _sendingItems.Position = 0;
            Send(_sendingItems);
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
            finally
            {
                e.SetBuffer(0, _receiveBuffer.Length);
                if (!_socket.ReceiveFromAsync(e))
                    OnReceiveCompleted(this, e);
            }
        }

        private void OnError(Exception e) 
            => Error?.Invoke(this, new ErrorEventArgs(e));
        
        private void OnDataReceived(byte[] data, int offset, int length)
        {
            DataReceived?.Invoke(this, new DataEventArgs
            {
                Data = data,
                Offset = offset,
                Length = length
            });
        }
    }
}
