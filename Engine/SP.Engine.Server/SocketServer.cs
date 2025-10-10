using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SP.Common.Buffer;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;
using SP.Engine.Server.Configuration;

namespace SP.Engine.Server
{
    public interface ISocketServer
    {
        bool IsRunning { get; }
        bool Start();
        void Stop();
    }

    internal sealed class SocketServer(IBaseEngine engine, ListenerInfo[] listenerInfos) : ISocketServer, IDisposable
    {
        private IBaseEngine Engine { get; } = engine;
        private List<ISocketListener> Listeners { get; } = new(listenerInfos.Length);
        private ListenerInfo[] ListenerInfos { get; } = listenerInfos;
        private bool IsStopped { get; set; }

        public bool IsRunning { get; private set; }
        
        internal IObjectPool<SegmentQueue> SendingQueuePool { get; private set; }

        private byte[] _keepAliveOptionValues;        
        private ConcurrentStack<SocketReceiveContext> _socketReceiveContextPool;

        public bool Start()
        {
            IsStopped = false;

            var config = Engine.Config;
            var logger = Engine.Logger;

            var sendingQueuePool = new ExpandablePool<SegmentQueue>();
            sendingQueuePool.Initialize(Math.Max(config.Network.LimitConnectionCount / 6, 256)
                , Math.Max(config.Network.LimitConnectionCount * 2, 256)
                , new SendingQueueSegmentCreator(config.Network.SendingQueueSize));

            SendingQueuePool = sendingQueuePool;

            foreach (var info in ListenerInfos)
            {
                var listener = CreateListener(info);
                listener.Error += OnListenerError;
                listener.Stopped += OnListenerStopped;
                listener.NewClientAccepted += OnNewClientAccepted;

                if (listener.Start())
                {
                    Listeners.Add(listener);
                    logger.Info("Listener ({0}:{1}) was started.", listener.EndPoint, listener.Mode);
                }
                else
                {
                    logger.Error("Listener ({0}:{1}) failed to start.", listener.EndPoint, listener.Mode);

                    foreach (var t in Listeners)
                        t.Stop();

                    Listeners.Clear();
                    return false;
                }
            }

            if (!SetupKeepAlive(config))
                return false;

            if (!SetupSocketEventArgsPool(config))
                return false;

            IsRunning = true;
            return true;
        }

        private bool SetupKeepAlive(IEngineConfig config)
        {
            try
            {
                var isKeepAlive = config.Network.EnableKeepAlive ? 1 : 0; 
                var keepAliveTime = (uint)config.Network.KeepAliveTimeSec * 1000;
                var keepAliveInterval = (uint)config.Network.KeepAliveIntervalSec * 1000;
                const uint dummy = 0;
                _keepAliveOptionValues = new byte[Marshal.SizeOf(dummy) * 3];
                BitConverter.GetBytes((uint)isKeepAlive).CopyTo(_keepAliveOptionValues, 0); // 활성화 (1=true)
                BitConverter.GetBytes(keepAliveTime).CopyTo(_keepAliveOptionValues, Marshal.SizeOf(dummy)); // 유휴 시간 (ms)
                BitConverter.GetBytes(keepAliveInterval).CopyTo(_keepAliveOptionValues, Marshal.SizeOf(dummy) * 2); // Keep-Alive 패킷 간격 (ms)
                return true;
            }
            catch (Exception ex)
            {
                Engine.Logger.Fatal("An exception occurred: {0}\r\nstackTrace: {1}", ex.Message, ex.StackTrace);
                return false;
            }
        }

        private bool SetupSocketEventArgsPool(IEngineConfig config)
        {
            var bufferSize = config.Network.ReceiveBufferSize;
            var limitCount = config.Network.LimitConnectionCount;
            var totalBytes = bufferSize * limitCount;
            var buffer = new byte[totalBytes];

            var currentOffset = 0;
            var contexts = new List<SocketReceiveContext>(limitCount);
            for (var i = 0; i < limitCount; i++)
            {
                var socketEventArgs = new SocketAsyncEventArgs();
                if (totalBytes - bufferSize < currentOffset)
                    return false;

                socketEventArgs.SetBuffer(buffer, currentOffset, bufferSize);
                currentOffset += bufferSize;

                contexts.Add(new SocketReceiveContext(socketEventArgs));
            }

            _socketReceiveContextPool = new ConcurrentStack<SocketReceiveContext>(contexts);
            return true;
        }

        public void Stop()
        {
            IsStopped = true;

            foreach (var listener in Listeners)
                listener.Stop();

            Listeners.Clear();
            SendingQueuePool = null;

            if (null != _socketReceiveContextPool)
            {
                foreach (var context in _socketReceiveContextPool)
                    context.SocketEventArgs.Dispose();

                _socketReceiveContextPool.Clear();
            }            

            IsRunning = false;
        }

        public void Dispose()
        {
            if (IsRunning)
                Stop();
        }

        private static ISocketListener CreateListener(ListenerInfo listenerInfo)
        {
            return listenerInfo.Mode switch
            {
                SocketMode.Tcp => new TcpNetworkListener(listenerInfo),
                SocketMode.Udp => new UdpNetworkListener(listenerInfo),
                _ => throw new ArgumentException($"Invalid socket mode: {listenerInfo.Mode}"),
            };
        }

        private void OnNewClientAccepted(ISocketListener listener, Socket socket, object state)
        {
            if (IsStopped)
                return;

            switch (listener.Mode)
            {
                case SocketMode.Tcp:
                    ProcessTcpClient(socket);
                    break;
                case SocketMode.Udp:
                    ProcessUdpClient(socket, state);
                    break;
                default:
                    throw new NotSupportedException($"{listener.Mode}");
            }            
        }
        
        private void ProcessUdpClient(Socket socket, object state)
        {
            if (state is not (byte[] datagram, IPEndPoint remoteEndPoint))
                return;
            
            Engine.ProcessUdpClient(datagram, socket, remoteEndPoint);
        }

        private void ProcessTcpClient(Socket client)
        {
            if (!_socketReceiveContextPool.TryPop(out var context))
            {
                Engine.AsyncRun(client.SafeClose);
                Engine.Logger.Error("Limit connection count {0} was reached.", Engine.Config.Network.LimitConnectionCount);
                return;
            }

            var networkSession = new TcpNetworkSession(client, context);
            networkSession.Closed += OnSessionClosed;

            var session = CreateSession(client, networkSession);
            if (RegisterSession(session))
                Engine.AsyncRun(networkSession.Start);
            else
            {
                networkSession.Close(CloseReason.ApplicationError);
            }
        }

        private void OnSessionClosed(INetworkSession session, CloseReason reason)
        {
            if (session is not ITcpNetworkSession networkSession) return;
            var context = networkSession.ReceiveContext;
            context.Reset();
            _socketReceiveContextPool?.Push(context);
        }

        private void OnListenerStopped(object sender, EventArgs e)
        {
            if (sender is ISocketListener listener)
                Engine.Logger.Info("Listener ({0}) was stopped.", listener.EndPoint);            
        }

        private void OnListenerError(ISocketListener listener, Exception e)
        {
            Engine.Logger.Error($"Listener ({listener.EndPoint}) error: {e.Message}\r\nstackTrace={e.StackTrace}");
        }

        private IBaseSession CreateSession(Socket client, TcpNetworkSession networkSession)
        {
            var config = Engine.Config;
            if (0 < config.Network.SendTimeoutMs)
                client.SendTimeout = config.Network.SendTimeoutMs;

            if (0 < config.Network.ReceiveBufferSize)
                client.ReceiveBufferSize = config.Network.ReceiveBufferSize;

            if (0 < config.Network.SendBufferSize)
                client.SendBufferSize = config.Network.SendBufferSize;

            client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, _keepAliveOptionValues);

            var lingerOption = new LingerOption(false, 0); // 즉시 닫기
            client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, lingerOption); 
            client.NoDelay = true;
            return Engine.CreateSession(networkSession);
        }
        
        private bool RegisterSession(IBaseSession session)
        {
            if (Engine.RegisterSession(session))
                return true;

            session.NetworkSession.Close(CloseReason.InternalError);
            return false;
        }

    }
}
