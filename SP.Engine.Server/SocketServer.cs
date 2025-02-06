using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SP.Engine.Common;
using SP.Engine.Common.Logging;
using SP.Engine.Core;
using SP.Engine.Core.Message;

namespace SP.Engine.Server
{
    public interface ISocketServer
    {
        bool IsRunning { get; }
        bool Start();
        void Stop();
    }

    internal sealed class SocketServer : ISocketServer, IDisposable
    {
        public ISessionServer Server { get; private set; }
        public bool IsRunning { get; private set; }
        private List<ISocketListener> Listeners { get; }
        private ListenerInfo[] ListenerInfos { get; }
        private bool IsStopped { get; set; }

        internal ISmartPool<SendingQueue> SendingQueuePool { get; private set; }

        private byte[] _keepAliveOptionValues;        
        private byte[] _readWriteBuffer;
        private ConcurrentStack<SocketAsyncEventArgsProxy> _readWritePool;
        
        public SocketServer(ISessionServer server, ListenerInfo[] listenerInfos)
        {
            Server = server;
            ListenerInfos = listenerInfos;
            Listeners = new List<ISocketListener>(listenerInfos.Length);
        }

        public bool Start()
        {
            IsStopped = false;
           
            var config = Server.Config;
            var logger = Server.Logger;

            // 동접 3000명 기준으로 초기 500개만 만들고 이후 동접수가 증가함에 따라 6000개 까지 전송 큐 생성가능.
            // 최소 256개의 풀 생성함
            var sendingQueuePool = new SmartPool<SendingQueue>();
            sendingQueuePool.Initialize(Math.Max(config.LimitConnectionCount / 6, 256)
                , Math.Max(config.LimitConnectionCount * 2, 256)
                , new SendingQueueSourceCreator(config.SendingQueueSize));

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
                    logger.WriteLog(ELogLevel.Debug, "Listener ({0}) was started.", listener.EndPoint);
                }
                else
                {
                    logger.WriteLog(ELogLevel.Debug, "Listener ({0}) failed to start.", listener.EndPoint);

                    foreach (var t in Listeners)
                        t.Stop();

                    Listeners.Clear();
                    return false;
                }
            }

            if (!SetupKeepAlive(config))
                return false;

            if (!SetupReadWritePool(config))
                return false;

            IsRunning = true;
            return true;
        }

        private bool SetupKeepAlive(IServerConfig config)
        {
            try
            {
                var keepAliveTime = (uint)config.KeepAliveTimeSec * 1000;
                var keepAliveInterval = (uint)config.KeepAliveIntervalSec * 1000;
                const uint dummy = 0;
                _keepAliveOptionValues = new byte[Marshal.SizeOf(dummy) * 3];
                BitConverter.GetBytes((uint)1).CopyTo(_keepAliveOptionValues, 0); // 활성화 (1=true)
                BitConverter.GetBytes(keepAliveTime).CopyTo(_keepAliveOptionValues, Marshal.SizeOf(dummy)); // 유휴 시간 (ms)
                BitConverter.GetBytes(keepAliveInterval).CopyTo(_keepAliveOptionValues, Marshal.SizeOf(dummy) * 2); // Keep-Alive 패킷 간격 (ms)
                return true;
            }
            catch (Exception ex)
            {
                Server.Logger.WriteLog(ELogLevel.Fatal, "An exception occurred: {0}\r\nstackTrace: {1}", ex.Message, ex.StackTrace);
                return false;
            }
        }

        private bool SetupReadWritePool(IServerConfig config)
        {
            var totalBytes = config.ReceiveBufferSize * config.LimitConnectionCount;
            _readWriteBuffer = new byte[totalBytes];
            var bufferSize = config.ReceiveBufferSize;

            var currentOffset = 0;
            var proxies = new List<SocketAsyncEventArgsProxy>(config.LimitConnectionCount);
            for (var i = 0; i < config.LimitConnectionCount; i++)
            {
                var socketEventArgs = new SocketAsyncEventArgs();
                if (totalBytes - bufferSize < currentOffset)
                    return false;

                socketEventArgs.SetBuffer(_readWriteBuffer, currentOffset, bufferSize);
                currentOffset += bufferSize;

                proxies.Add(new SocketAsyncEventArgsProxy(socketEventArgs));
            }

            _readWritePool = new ConcurrentStack<SocketAsyncEventArgsProxy>(proxies);
            return true;
        }

        public void Stop()
        {
            IsStopped = true;

            foreach (var listener in Listeners)
                listener.Stop();

            Listeners.Clear();
            SendingQueuePool = null;

            if (null != _readWritePool)
            {
                foreach (var pool in _readWritePool)
                    pool.SocketEventArgs.Dispose();

                _readWritePool.Clear();
            }            

            _readWriteBuffer = null;

            IsRunning = false;
        }

        public void Dispose()
        {
            if (IsRunning)
                Stop();
        }

        private static ISocketListener CreateListener(ListenerInfo listenerInfo)
        {
            switch (listenerInfo.Mode)
            {
                case ESocketMode.Tcp:
                    return new TcpAsyncSocketListener(listenerInfo);
                case ESocketMode.Udp:
                    return new UdpSocketListener(listenerInfo);
                default:
                    throw new Exception($"Invalid socket mode: {listenerInfo.Mode}");
            }
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
            if (state is not object[] args ||
                args[0] is not byte[] data ||
                args[1] is not EndPoint remoteEndPoint)
            {
                return;   
            }
        }

        private void ProcessTcpClient(Socket client)
        {
            if (!_readWritePool.TryPop(out var proxy))
            {
                Server.AsyncRun(client.SafeClose);
                Server.Logger.WriteLog(ELogLevel.Error, "Limit connection count {0} was reached.", Server.Config.LimitConnectionCount);
                return;
            }

            var socketSession = new TcpAsyncSocketSession(client, proxy);
            socketSession.Closed += OnSessionClosed;

            var session = CreateSession(client, socketSession);
            if (RegisterSession(session))
                Server.AsyncRun(socketSession.Start);
        }

        private void OnSessionClosed(ISocketSession session, ECloseReason reason)
        {
            if (!(session is ITcpAsyncSocketSession socketSession)) return;
            var proxy = socketSession.ReceiveSocketEventArgsProxy;
            proxy.Reset();
            _readWritePool?.Push(proxy);
        }

        private void OnListenerStopped(object sender, EventArgs e)
        {
            if (sender is ISocketListener listener)
                Server.Logger.WriteLog(ELogLevel.Debug, $"Listener ({listener.EndPoint}) was stopped.");            
        }

        private void OnListenerError(ISocketListener listener, Exception e)
        {
            Server.Logger.WriteLog(ELogLevel.Error, $"Listener ({listener.EndPoint}) error: {e.Message}\r\nstackTrace={e.StackTrace}");
        }

        private ISession CreateSession(Socket client, ISocketSession socketSession)
        {
            var config = Server.Config;
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
            return Server.CreateSession(socketSession);
        }
        
        private bool RegisterSession(ISession session)
        {
            if (Server.RegisterSession(session))
                return true;

            session.SocketSession.Close(ECloseReason.InternalError);
            return false;
        }

    }
}
