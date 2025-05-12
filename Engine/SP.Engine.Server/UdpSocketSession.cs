using System.Net;
using System.Net.Sockets;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Utilities;

namespace SP.Engine.Server
{
    internal class UdpSocketSession : BaseSocketSession
    {
        private readonly Socket _socket;        

        public UdpSocketSession(Socket socket, IPEndPoint remoteEndPoint)
            : base (ESocketMode.Udp)
        {
            _socket = socket;
            RemoteEndPoint = remoteEndPoint;
            LocalEndPoint = (IPEndPoint)socket.LocalEndPoint;
        }        

        public void UpdateRemoteEndPoint(IPEndPoint remoteEndPoint)
        {
            RemoteEndPoint = remoteEndPoint;
        }

        public override void Start()
        {
        }

        protected override void Send(SendingQueue queue)
        {
            var e = new SocketAsyncEventArgs();
            e.Completed += OnSendCompleted;
            e.RemoteEndPoint = RemoteEndPoint;
            e.UserToken = queue;

            var item = queue[queue.Position];
            e.SetBuffer(item.Array, item.Offset, item.Count);

            if (!_socket.SendToAsync(e))
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
            if (e.UserToken is not SendingQueue queue)
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
