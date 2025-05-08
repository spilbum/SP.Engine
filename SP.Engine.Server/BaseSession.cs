using System;
using System.Collections.Generic;
using System.Net;
using SP.Common.Logging;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Serialization;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Handler;

namespace SP.Engine.Server
{
    public interface ISession : ILogContext, IHandleContext
    {
        string SessionId { get; }
        IPEndPoint LocalEndPoint { get; }
        IPEndPoint RemoteEndPoint { get; }
        IEngineConfig Config { get; }
        IEngine Engine { get; }
        ISocketSession SocketSession { get; }
    
        void ProcessBuffer(byte[] buffer, int offset, int length);
        bool TrySend(byte[] data);
        void Reject(ERejectReason reason, string detailReason = null);
        void Close(ECloseReason reason);
    }
    
    public abstract class BaseSession<TSession> : ISession
        where TSession : BaseSession<TSession>, ISession, new()
    {
        private BinaryBuffer _receiveBuffer;
        private BaseEngine<TSession> Engine { get; set; }

        IEngine ISession.Engine => Engine;
        public IEngineConfig Config => Engine.Config;
        public ILogger Logger => Engine.Logger;
        public IPEndPoint LocalEndPoint => SocketSession.LocalEndPoint;
        public IPEndPoint RemoteEndPoint => SocketSession.RemoteEndPoint;
        public bool IsConnected { get; internal set; }
        public DateTime StartTime { get; }
        public DateTime LastActiveTime { get; private set; }
        public ISocketSession SocketSession { get; private set; }
        public string SessionId { get; private set; }
        public DateTime StartClosingTime { get; protected set; }
        public bool IsAuthorized { get; private set; }
        public ERejectReason RejectReason { get; private set; }
        public string RejectDetailReason { get; private set; }
        
        protected BaseSession()
        {
            StartTime = DateTime.UtcNow;
            LastActiveTime = StartTime;            
        }

        public virtual void Initialize(IEngine engine, ISocketSession socketSession)
        {            
            Engine = (BaseEngine<TSession>)engine;
            SocketSession = socketSession;
            SessionId = socketSession.SessionId; 
            _receiveBuffer = new BinaryBuffer(engine.Config.LimitRequestLength);
            socketSession.Initialize(this);
            IsConnected = true;

            OnInit();
        }

        protected virtual void OnInit()
        {

        }

        public void SetAuthorized()
        {
            IsAuthorized = true;
        }

        public virtual bool TrySend(byte[] data)
        {
            return TrySend(new ArraySegment<byte>(data, 0, data.Length));
        }

        

        internal virtual bool TryInternalSend(IProtocolData protocolData)
        {
            try
            {
                var message = new TcpMessage();
                message.SerializeProtocol(protocolData, null);
                var bytes = message.ToArray();
                return null != bytes && TrySend(new ArraySegment<byte>(bytes, 0, bytes.Length));
            }
            catch (Exception e)
            {
                Logger.Error(e);
                return false;
            }
        }
        

        private bool TrySend(ArraySegment<byte> segment)
        {
            if (!SocketSession.TrySend(segment))
                return false;

            LastActiveTime = DateTime.UtcNow;
            return true;
        }

        void ISession.ProcessBuffer(byte[] buffer, int offset, int length)
        {
            try
            {
                foreach (var message in Filter(buffer, offset, length))
                {
                    try
                    {
                        ExecuteMessage(message);
                    }
                    catch (Exception ex)
                    {
                        Logger.Error(ex);
                    }
                    finally
                    {
                        LastActiveTime = DateTime.UtcNow;
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
        }
        
        private IEnumerable<TcpMessage> Filter(byte[] buffer, int offset, int length) 
        {
            _receiveBuffer.Write(buffer.AsSpan(offset, length));
            
            while (true)
            {
                var message = TcpMessage.TryReadBuffer(_receiveBuffer, out var totalLength);
                if (message != null)
                {
                    if (totalLength >= Config.LimitRequestLength)
                    {
                        Logger.Error("Max request length: {0}, current processed length: {1}", Config.LimitRequestLength, totalLength);
                        Close(ECloseReason.ProtocolError);
                        yield return null;
                    }
                    
                    yield return message;
                }
                else
                    break;
            }

            if (_receiveBuffer.RemainSize < 1024)
                _receiveBuffer.Trim();
        }

        protected abstract void ExecuteMessage(IMessage message);

        public void Reject(ERejectReason reason, string detailReason = null)
        {
            RejectReason = reason;
            RejectDetailReason = detailReason;
            Logger.Debug("Session is rejected. reason:{0}, detail:{1}", reason, detailReason);
            Close(ECloseReason.Rejected);
        }

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
