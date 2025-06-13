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
        private SocketAsyncEventArgs _sendEventArgs;
        private readonly PostList<ArraySegment<byte>> _sendingItems = new PostList<ArraySegment<byte>>();
        private ConcurrentBatchQueue<ArraySegment<byte>> _sendQueue;
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
                _sendQueue = new ConcurrentBatchQueue<ArraySegment<byte>>(300);
                _receiveBuffer = new byte[ushort.MaxValue];
            
                _socket = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                _socket.Connect(_remoteEndPoint);

                _receiveEventArgs.SetBuffer(_receiveBuffer, 0, _receiveBuffer.Length);
                _receiveEventArgs.RemoteEndPoint = new IPEndPoint(IPAddress.Any, 0);
                _receiveEventArgs.Completed += OnReceiveCompleted;
                _socket.ReceiveFromAsync(_receiveEventArgs);
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
            CheckKeepAlive();
            Assembler.Cleanup(TimeSpan.FromSeconds(10));
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
            if (!_sendQueue.Enqueue(items))
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
        
        private List<ArraySegment<byte>> ToSegments(UdpMessage message)
        {
            var segments = new List<ArraySegment<byte>>();
            if (message.Length <= _mtu)
            {
                segments.Add(message.Payload);
                return segments;
            }

            var fragmentId = (uint)Interlocked.Increment(ref _nextFragmentId);
            segments.AddRange(message.ToSplit(_mtu, fragmentId).Select(f => f.Serialize()));
            return segments;
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
                OnError(new SocketException((int)e.SocketError));
                return;
            }
            
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
            
            _sendQueue.DequeueAll(_sendingItems);
            if (_sendingItems.Count == 0)
            {
                Interlocked.Exchange(ref _isSending, 0);
                return;
            }

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
