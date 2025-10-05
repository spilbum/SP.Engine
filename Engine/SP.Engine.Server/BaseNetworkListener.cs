using System;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;
using SP.Common.Logging;

namespace SP.Engine.Server
{
    internal delegate void ErrorHandler(ISocketListener listener, Exception e);

    internal delegate void NewClientAcceptHandler(ISocketListener listener, Socket socket, object state);

    public class ListenerInfo
    {
        public IPEndPoint EndPoint { get; set; }
        public int BackLog { get; set; }
        public SocketMode Mode { get; set; }
    }

    internal interface ISocketListener
    {
        SocketMode Mode { get; set; }
        IPEndPoint EndPoint { get; }
        int BackLog { get; }
        event EventHandler Stopped;
        event ErrorHandler Error;
        event NewClientAcceptHandler NewClientAccepted;

        bool Start();
        void Stop();
    }

    internal abstract class BaseNetworkListener(ListenerInfo info) : ISocketListener, IDisposable
    {
        public SocketMode Mode { get; set; } = info.Mode;
        public IPEndPoint EndPoint { get; } = info.EndPoint;
        public int BackLog { get; } = info.BackLog;

        public event EventHandler Stopped;
        public event ErrorHandler Error;
        public event NewClientAcceptHandler NewClientAccepted;

        public abstract bool Start();
        public abstract void Stop();

        protected void OnStopped() => Stopped?.Invoke(this, EventArgs.Empty);
        protected void OnError(Exception e) => Error?.Invoke(this, e);

        protected void OnNewClientAccepted(Socket socket, object state)
        {
            var handler = NewClientAccepted;
            if (handler == null) return;
            
            switch (Mode)
            {
                case SocketMode.Tcp:
                    handler.Invoke(this, socket, state);
                    break;
                case SocketMode.Udp:
                    Task.Run(() => handler.Invoke(this, socket, state));
                    break;
            }
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }


        protected virtual void Dispose(bool disposing)
        {
            OnStopped();
        }
    }
}
