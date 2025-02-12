using System;
using System.Net.Sockets;

namespace SP.Engine.Server
{

    internal class SocketAsyncEventArgsProxy
    {
        public SocketAsyncEventArgs SocketEventArgs { get; } 
        public int OriginOffset { get; private set; }

        public SocketAsyncEventArgsProxy(SocketAsyncEventArgs e)
        {
            SocketEventArgs = e;
            OriginOffset = e.Offset;
            SocketEventArgs.Completed += OnReceiveCompleted;
        }

        private static void OnReceiveCompleted(object sender, SocketAsyncEventArgs e)
        {
            if (e.UserToken is not ITcpAsyncSocketSession socketSession)
                return;

            if (e.LastOperation == SocketAsyncOperation.Receive)
                socketSession.AsyncRun(() => socketSession.ProcessReceive(e));
            else
                throw new ArgumentException("The last operation completed on the socket was not a receive");
        }

        public void Initialize(ITcpAsyncSocketSession socketSession)
        {
            SocketEventArgs.UserToken = socketSession;
        }

        public void Reset()
        {
            SocketEventArgs.UserToken = null;
        }
    }
    
}
