using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server
{
    public class UdpSocket : BaseNetworkSession, IUdpSender
    {
        private uint _nextFragmentId;
        private ushort _mtu;
        
        public UdpSocket(Socket client, IPEndPoint remoteEndPoint)
            : base (ESocketMode.Udp, client)
        {
            RemoteEndPoint = remoteEndPoint;
            LocalEndPoint = (IPEndPoint)client.LocalEndPoint;
        }
        
        public bool TrySend(UdpMessage message)
        {
            var segments = ToSegments(message);
            return segments.All(TrySend);
        }
        
        private List<ArraySegment<byte>> ToSegments(UdpMessage message)
        {
            var segments = new List<ArraySegment<byte>>();
            if (message.Length <= _mtu)
            {
                segments.Add(message.Payload);
                return segments;
            }

            var fragmentId = Interlocked.Increment(ref _nextFragmentId);
            segments.AddRange(message.ToSplit(_mtu, fragmentId).Select(f => f.Serialize()));
            return segments;
        }

        public void SetMtu(ushort mtu)
        {
            _mtu = mtu;
        }
        
        public void UpdateRemoteEndPoint(IPEndPoint remoteEndPoint)
        {
            if (RemoteEndPoint != null && RemoteEndPoint.ToString() == remoteEndPoint.ToString())
                return;
            
            RemoteEndPoint = remoteEndPoint;
        }

        public override void Start()
        {
        }

        protected override void Send(SegmentQueue queue)
        {
            var e = new SocketAsyncEventArgs();
            e.Completed += OnSendCompleted;
            e.RemoteEndPoint = RemoteEndPoint;
            e.UserToken = queue;
            
            var item = queue[queue.Position];
            e.SetBuffer(item.Array, item.Offset, item.Count);
            
            var client = Client;
            if (client == null)
            {
                OnSendError(queue, CloseReason.SocketError);
                return;
            }
            
            if (!client.SendToAsync(e))
                OnSendCompleted(this, e);
        }

        private void ClearSocketAsyncEventArgs(SocketAsyncEventArgs e)
        {
            e.UserToken = null;
            e.Completed -= OnSendCompleted;
            e.Dispose();
        }

        private void OnSendCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.UserToken is not SegmentQueue queue)
                return;

            if (e.SocketError != SocketError.Success)
            {
                LogError(new SocketException((int)e.SocketError));
                ClearSocketAsyncEventArgs(e);
                OnSendError(queue, CloseReason.SocketError);
                return;
            }

            ClearSocketAsyncEventArgs(e);

            var newPos = queue.Position + 1;
            if (newPos >= queue.Count)
            {
                OnSendCompleted(queue);
                return;
            }

            queue.SetPosition(newPos);
            Send(queue);
        }

        protected override bool TryValidateClosedBySocket(out Socket client)
        {
            client = null;
            return false;
        }
    }
}
