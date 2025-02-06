using System;
using System.Collections.Concurrent;
using System.Net;
using SP.Engine.Common;
using SP.Engine.Common.Logging;
using SP.Engine.Core;
using SP.Engine.Core.Message;
using SP.Engine.Core.Protocol;

namespace SP.Engine.Server
{
    public interface ISession : ILoggerProvider
    {
        IServerConfig Config { get; }
        ISessionServer SessionServer { get; }
        ISocketSession SocketSession { get; }
        DateTime LastActiveTime { get; }
        DateTime StartTime { get; }
    
        void ProcessBuffer(byte[] buffer, int offset, int length);
    }
    
    public abstract class SessionBase<TSession> : ISession
        where TSession : SessionBase<TSession>, ISession, new()
    {
        private string _sessionId;
        private ISocketSession _socketSession;
        private SessionServerBase<TSession> _sessionServer;
        private MessageFilter _messageFilter;
        
        private SessionServerBase<TSession> SessionServer => _sessionServer ?? throw new NullReferenceException(nameof(_sessionServer));
        ISessionServer ISession.SessionServer => SessionServer;
        public IServerConfig Config => SessionServer.Config;
        public ILogger Logger => SessionServer.Logger;
        public IPEndPoint LocalEndPoint => SocketSession.LocalEndPoint;
        public IPEndPoint RemoteEndPoint => SocketSession.RemoteEndPoint;
        public bool IsConnected { get; internal set; }
        public DateTime StartTime { get; }
        public DateTime LastActiveTime { get; private set; }
        public ISocketSession SocketSession => _socketSession ?? throw new NullReferenceException(nameof(_socketSession));
        public string SessionId => _sessionId ?? throw new NullReferenceException(nameof(_sessionId));
        public DateTime StartClosingTime { get; protected set; }
        public bool IsAuthorized { get; protected set; }
        
        protected SessionBase()
        {
            StartTime = DateTime.UtcNow;
            LastActiveTime = StartTime;            
        }

        public virtual void Initialize(ISessionServer server, ISocketSession socketSession)
        {            
            _sessionServer = (SessionServerBase<TSession>)server;
            _messageFilter = new MessageFilter(server.Config.LimitRequestLength);
            _socketSession = socketSession;
            _sessionId = socketSession.SessionId;            
            socketSession.Initialize(this);
            IsConnected = true;

            OnInit();
        }

        protected virtual void OnInit()
        {

        }

        public virtual bool TrySend(byte[] data)
        {
            return TrySend(new ArraySegment<byte>(data, 0, data.Length));
        }

        internal virtual bool TryInternalSend(IProtocolData protocol)
        {
            try
            {
                var message = new TcpMessage();
                message.SerializeProtocol(protocol, null);
                var bytes = message.ToArray();
                return null != bytes && TrySend(new ArraySegment<byte>(bytes, 0, bytes.Length));
            }
            catch (Exception e)
            {
                Logger.WriteLog(e);
                return false;
            }
        }
        

        private bool TrySend(ArraySegment<byte> segment)
        {
            if (SocketSession.TrySend(segment))
                return false;

            LastActiveTime = DateTime.UtcNow;
            return true;
        }

        void ISession.ProcessBuffer(byte[] buffer, int offset, int length)
        {
            var filter = _messageFilter;
            if (null == filter)
                return;

            try
            {
                filter.AddBuffer(buffer, offset, length);
            
                while (true)
                {
                    var message = filter.Filter(out var left);
                    if (null != message)
                    {
                        try
                        {
                            OnMessageReceived(message);
                        }
                        catch (Exception ex)
                        {
                            Logger.WriteLog(ex);
                        }
                        finally
                        {
                            LastActiveTime = DateTime.UtcNow;
                        }
                    }

                    if (0 >= left)
                        break;
                }
            }
            catch (Exception e)
            {
                Logger.WriteLog(e);
            }
        }

        protected abstract void OnMessageReceived(IMessage message);

        public virtual void Close(ECloseReason reason)
        {
            SocketSession.Close(reason);
        }

        public virtual void Close()
        {
            Close(ECloseReason.ServerClosing);
        }      
    }
}
