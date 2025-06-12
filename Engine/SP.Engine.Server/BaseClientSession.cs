using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using SP.Common.Buffer;
using SP.Common.Logging;
using SP.Engine.Client;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Message;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.ProtocolHandler;

namespace SP.Engine.Server
{
    public interface IClientSession : ILogContext, IHandleContext
    {
        string SessionId { get; }
        IPEndPoint LocalEndPoint { get; }
        IPEndPoint RemoteEndPoint { get; }
        IEngineConfig Config { get; }
        IEngine Engine { get; }
        TcpNetworkSession TcpSession { get; }
        UdpNetworkSession UdpSession { get; }

        bool Send(IMessage message);
        void EnsureUdpSocket(Socket socket, IPEndPoint remoteEndPoint, ushort mtu);
        void ProcessMessage(IMessage message);
        void ProcessBuffer(byte[] buffer, int offset, int length);
        void Reject(ERejectReason reason, string detailReason = null);
        void Close(ECloseReason reason);
    }

    public abstract class BaseClientSession<TSession> : IClientSession
        where TSession : BaseClientSession<TSession>, IClientSession, new()
    {
        private BinaryBuffer _receiveBuffer;
        private BaseEngine<TSession> Engine { get; set; }

        IEngine IClientSession.Engine => Engine;
        public IEngineConfig Config => Engine.Config;
        public ILogger Logger => Engine.Logger;
        public IPEndPoint LocalEndPoint => TcpSession.LocalEndPoint;
        public IPEndPoint RemoteEndPoint => TcpSession.RemoteEndPoint;
        public bool IsConnected { get; internal set; }
        public DateTime StartTime { get; }
        public DateTime LastActiveTime { get; private set; }
        public string SessionId { get; private set; }
        public DateTime StartClosingTime { get; protected set; }
        public bool IsAuthorized { get; private set; }
        public ERejectReason RejectReason { get; private set; }
        public string RejectDetailReason { get; private set; }

        public TcpNetworkSession TcpSession { get; private set; }
        public UdpNetworkSession UdpSession { get; private set; }

        protected BaseClientSession()
        {
            StartTime = DateTime.UtcNow;
            LastActiveTime = StartTime;
        }

        public virtual void Initialize(IEngine engine, TcpNetworkSession networkSession)
        {
            Engine = (BaseEngine<TSession>)engine;
            TcpSession = networkSession;
            SessionId = networkSession.SessionId;
            _receiveBuffer = new BinaryBuffer(engine.Config.MaxAllowedLength);
            networkSession.Attach(this);
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

        public bool Send(IMessage message)
        {
            switch (message)
            {
                case TcpMessage tcpMessage:
                {
                    if (!TcpSession.TrySend(tcpMessage))
                        return false;
                    
                    break;
                }
                case UdpMessage udpMessage:
                {
                    if (UdpSession.TrySend(udpMessage))
                        return false;

                    break;
                }
            }
            
            LastActiveTime = DateTime.UtcNow;
            return true;
        }

        void IClientSession.EnsureUdpSocket(Socket socket, IPEndPoint remoteEndPoint, ushort mtu)
        {
            lock (this)
            {
                if (UdpSession == null)
                {
                    UdpSession = new UdpNetworkSession(socket, remoteEndPoint, mtu);
                    UdpSession.Attach(this);
                }
                else
                {
                    UdpSession.UpdateRemoteEndPoint(remoteEndPoint);
                }
            }
        }

        public void ProcessMessage(IMessage message)
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

        void IClientSession.ProcessBuffer(byte[] buffer, int offset, int length)
        {
            var span = buffer.AsSpan(offset, length);
            _receiveBuffer.Write(span);
            
            try
            {
                foreach (var message in Filter())
                    ProcessMessage(message);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
            finally
            {  
                if (_receiveBuffer.RemainSize < 1024)
                    _receiveBuffer.Trim();
            }
        }
        
        private IEnumerable<TcpMessage> Filter() 
        {
            while (true)
            {
                if (_receiveBuffer.RemainSize < TcpHeader.HeaderSize)
                    yield break;
                
                var headerSpan = _receiveBuffer.Peek(TcpHeader.HeaderSize);
                if (!TcpHeader.TryParse(headerSpan, out var header))
                    yield break;
                
                if (header.PayloadLength > Config.MaxAllowedLength)
                {
                    Logger.Warn("Max allowed length. maxAllowedLength={0}, payloadLength={1}", Config.MaxAllowedLength, header.PayloadLength);
                    Close(ECloseReason.ProtocolError);
                    yield break;
                }
                
                if (_receiveBuffer.RemainSize < TcpHeader.HeaderSize + header.PayloadLength)
                    yield break;

                var payload = _receiveBuffer.ReadBytes(TcpHeader.HeaderSize + header.PayloadLength);
                var message = new TcpMessage(header, new ArraySegment<byte>(payload));
                yield return message;
            }
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
            TcpSession.Close(reason);
            UdpSession.Close(reason);
        }

        public virtual void Close()
        {
            Close(ECloseReason.ServerClosing);
        }      
    }
}
