using System;
using System.Net;
using System.Net.Sockets;

namespace SP.Engine.Server
{
    internal delegate void ErrorHandler(ISocketListener listener, Exception e);

    internal delegate void NewClientAcceptHandler(ISocketListener listener, Socket socket, object state);

    public class ListenerInfo
    {
        public IPEndPoint EndPoint { get; set; }
        public int BackLog { get; set; }
        public ESocketMode Mode { get; set; }
    }

    internal interface ISocketListener
    {
        ESocketMode Mode { get; set; }
        IPEndPoint EndPoint { get; }
        int BackLog { get; }
        event EventHandler Stopped;
        event ErrorHandler Error;
        event NewClientAcceptHandler NewClientAccepted;

        bool Start();
        void Stop();
    }

    internal abstract class SocketListenerBase : ISocketListener
    {
        public ESocketMode Mode { get; set; }
        public IPEndPoint EndPoint { get; private set; }
        public int BackLog { get; private set; }

        public event EventHandler Stopped;
        public event ErrorHandler Error;
        public event NewClientAcceptHandler NewClientAccepted;
        
        protected SocketListenerBase(ListenerInfo info)
        {
            EndPoint = info.EndPoint ?? throw new Exception("EndPoint is null");
            BackLog = info.BackLog;
            Mode = info.Mode;
        }
        
        public abstract bool Start();
        public abstract void Stop();

        protected void OnStopped() => Stopped?.Invoke(this, EventArgs.Empty);
        protected void OnError(Exception e) => Error?.Invoke(this, e);

        protected virtual void OnNewClientAccepted(Socket socket, object state)
        {
            var handler = NewClientAccepted;
            switch (Mode)
            {
                case ESocketMode.Tcp:
                    handler?.Invoke(this, socket, state);
                    break;
                case ESocketMode.Udp:
                    handler?.BeginInvoke(this, socket, state, null, null);
                    break;
            }
        }

        protected virtual void Dispose()
        {
            OnStopped();
        }
    }
}
