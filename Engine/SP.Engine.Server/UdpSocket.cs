using System;
using System.Net;
using System.Net.Sockets;
using SP.Engine.Runtime;

namespace SP.Engine.Server
{
    public class UdpSocket : BaseNetworkSession
    {
        public ushort Mtu { get; }
        
        public UdpSocket(Socket client, IPEndPoint remoteEndPoint, ushort mtu)
            : base (ESocketMode.Udp, client)
        {
            RemoteEndPoint = remoteEndPoint;
            LocalEndPoint = (IPEndPoint)client.LocalEndPoint;
            Mtu = mtu;
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
                OnSendError(queue, ECloseReason.SocketError);
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
                OnSendError(queue, ECloseReason.SocketError);
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
