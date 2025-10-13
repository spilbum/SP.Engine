using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Server
{
    public class UdpSocket : BaseNetworkSession, IUnreliableSender
    {
        private uint _fragSeq;
        private ushort _maxFrameSize = 512;
        private readonly FragmentAssembler _assembler = new();
        
        public IFragmentAssembler Assembler => _assembler;
        
        public UdpSocket(Socket client, IPEndPoint remoteEndPoint)
            : base (SocketMode.Udp, client)
        {
            RemoteEndPoint = remoteEndPoint;
            LocalEndPoint = (IPEndPoint)client.LocalEndPoint;
        }
        
        private uint AllocateFragId()
            => Interlocked.Increment(ref _fragSeq);
        
        public bool TrySend(UdpMessage message)
        {
            var items = new List<ArraySegment<byte>>();
            if (message.FrameLength <= _maxFrameSize)
            {
                items.Add(message.ToArraySegment());
            }
            else
            {
                var fragId = AllocateFragId();
                var maxFragBodyLen = (ushort)(_maxFrameSize - UdpHeader.ByteSize - FragmentHeader.ByteSize);
                items.AddRange(message.Split(fragId, maxFragBodyLen));
            }
            
            return items.All(TrySend);
        }
        
        public void SetMaxFrameSize(ushort mtu)
        {
            _maxFrameSize = (ushort)(mtu - 20 /* IP header size */ - 8 /* UDP header size*/);;
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
