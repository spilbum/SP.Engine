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

    internal sealed class SocketServer(IEngine engine, ListenerInfo[] listenerInfos) : ISocketServer, IDisposable
    {
        private IEngine Engine { get; } = engine;
        public bool IsRunning { get; private set; }
        private List<ISocketListener> Listeners { get; } = new List<ISocketListener>(listenerInfos.Length);
        private ListenerInfo[] ListenerInfos { get; } = listenerInfos;
        private bool IsStopped { get; set; }

        internal IObjectPool<SegmentQueue> SendingQueuePool { get; private set; }

        private byte[] _keepAliveOptionValues;        
        private ConcurrentStack<SocketReceiveContext> _socketReceiveContextPool;

        public bool Start()
        {
            IsStopped = false;
           
            var config = Engine.Config;
            var logger = Engine.Logger;

            var sendingQueuePool = new ExpandablePool<SegmentQueue>();
            sendingQueuePool.Initialize(Math.Max(config.LimitConnectionCount / 6, 256)
                , Math.Max(config.LimitConnectionCount * 2, 256)
                , new SendingQueueSegmentCreator(config.SendingQueueSize));

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
                var isKeepAlive = config.IsDisableKeepAlive ? 0 : 1; 
                var keepAliveTime = (uint)config.KeepAliveTimeSec * 1000;
                var keepAliveInterval = (uint)config.KeepAliveIntervalSec * 1000;
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
            var totalBytes = config.ReceiveBufferSize * config.LimitConnectionCount;
            var buffer = new byte[totalBytes];
            var bufferSize = config.ReceiveBufferSize;

            var currentOffset = 0;
            var contexts = new List<SocketReceiveContext>(config.LimitConnectionCount);
            for (var i = 0; i < config.LimitConnectionCount; i++)
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
                ESocketMode.Tcp => new TcpNetworkListener(listenerInfo),
                ESocketMode.Udp => new UdpNetworkListener(listenerInfo),
                _ => throw new ArgumentException($"Invalid socket mode: {listenerInfo.Mode}"),
            };
        }

        private void OnNewClientAccepted(ISocketListener listener, Socket socket, object state)
        {
            if (IsStopped)
                return;

            switch (listener.Mode)
            {
                case ESocketMode.Tcp:
                    ProcessTcpClient(socket);
                    break;
                case ESocketMode.Udp:
                    ProcessUdpClient(socket, state);
                    break;
                default:
                    throw new NotSupportedException($"{listener.Mode}");
            }            
        }
        
        private void ProcessUdpClient(Socket socket, object state)
        {
            if (state is not (byte[] buffer, IPEndPoint remoteEndPoint))
                return;

            var headerSpan = buffer.AsSpan();
            if (!UdpHeader.TryParse(headerSpan, out var header))
            {
                Engine.Logger.Warn($"Failed to parse UdpHeader: {buffer.Length} bytes");
                return;
            }
            
            var peer = Engine.GetPeer(header.PeerId);
            var session = peer?.Session;
            if (session == null)
                return;
            
            session.EnsureUdpSocket(socket, remoteEndPoint, peer.UdpMtu);

            if (header.IsFragmentation)
            {
                if (!UdpFragment.TryParse(buffer, out var fragment))
                {
                    Engine.Logger.Warn($"Failed to parse fragmentation: {buffer.Length} bytes");
                    return;
                }

                if (!peer.Assembler.TryAssemble(fragment, out var payload))
                    return;
                
                var message = new UdpMessage(header, payload);
                session.ProcessMessage(message);
            }
            else
            {
                var payload = new ArraySegment<byte>(buffer);
                var message = new UdpMessage(header, payload);
                session.ProcessMessage(message);
            }
        }

        private void ProcessTcpClient(Socket client)
        {
            if (!_socketReceiveContextPool.TryPop(out var context))
            {
                Engine.AsyncRun(client.SafeClose);
                Engine.Logger.Error("Limit connection count {0} was reached.", Engine.Config.LimitConnectionCount);
                return;
            }

            var networkSession = new TcpNetworkSession(client, context);
            networkSession.Closed += OnSessionClosed;

            var session = CreateSession(client, networkSession);
            if (RegisterSession(session))
                Engine.AsyncRun(networkSession.Start);
            else
            {
                networkSession.Close(ECloseReason.ApplicationError);
            }
        }

        private void OnSessionClosed(INetworkSession session, ECloseReason reason)
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

        private IClientSession CreateSession(Socket client, TcpNetworkSession networkSession)
        {
            var config = Engine.Config;
            if (0 < config.SendTimeOutMs)
                client.SendTimeout = config.SendTimeOutMs;

            if (0 < config.ReceiveBufferSize)
                client.ReceiveBufferSize = config.ReceiveBufferSize;

            if (0 < config.SendBufferSize)
                client.SendBufferSize = config.SendBufferSize;

            client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, _keepAliveOptionValues);

            var lingerOption = new LingerOption(false, 0); // 즉시 닫기
            client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.Linger, lingerOption); 
            client.NoDelay = true;
            return Engine.CreateSession(networkSession);
        }
        
        private bool RegisterSession(IClientSession clientSession)
        {
            if (Engine.RegisterSession(clientSession))
                return true;

            clientSession.TcpSession.Close(ECloseReason.InternalError);
            return false;
        }

    }
}
