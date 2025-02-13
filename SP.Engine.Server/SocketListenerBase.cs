﻿using System;
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

    internal abstract class SocketListenerBase(ListenerInfo info) : ISocketListener, IDisposable
    {
        public ESocketMode Mode { get; set; } = info.Mode;
        public IPEndPoint EndPoint { get; private set; } = info.EndPoint;
        public int BackLog { get; private set; } = info.BackLog;

        public event EventHandler Stopped;
        public event ErrorHandler Error;
        public event NewClientAcceptHandler NewClientAccepted;

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
