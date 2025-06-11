using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using SP.Common.Buffer;
using SP.Common.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Message;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.ProtocolHandler;

namespace SP.Engine.Server
{
    public static class ExtenstionMethod
    {
        public static IEnumerable<ArraySegment<byte>> ToSegments(this UdpMessage message, ushort mtu)
        {
            if (message.Length <= mtu)
            {
                yield return message.Payload;
                yield break;
            }

            foreach (var fragment in message.ToSplit(mtu))
            {
                var buffer = new byte[UdpHeader.HeaderSize + fragment.Length];
                message.Header.WriteTo(buffer);
                fragment.WriteTo(buffer);
                yield return new ArraySegment<byte>(buffer, 0, buffer.Length);
            }
        }

        public static ArraySegment<byte> ToSegment(this TcpMessage message)
        {
            var buffer = new byte[message.Length];
            message.WriteTo(buffer.AsSpan());
            return new ArraySegment<byte>(buffer);
        }
    }
    
    public interface IClientSession : ILogContext, IHandleContext
    {
        string SessionId { get; }
        IPEndPoint LocalEndPoint { get; }
        IPEndPoint RemoteEndPoint { get; }
        IEngineConfig Config { get; }
        IEngine Engine { get; }
        TcpNetworkSession TcpSession { get; }
        UdpSocket UdpSocket { get; }

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
        public UdpSocket UdpSocket { get; private set; }

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
                    if (!TcpSession.TrySend(tcpMessage.ToSegment()))
                        return false;
                    
                    break;
                }
                case UdpMessage udpMessage:
                {
                    var segments = udpMessage.ToSegments(UdpSocket.Mtu);
                    if (segments.Any(segment => !UdpSocket.TrySend(segment)))
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
                if (UdpSocket == null)
                {
                    UdpSocket = new UdpSocket(socket, remoteEndPoint, mtu);
                    UdpSocket.Attach(this);
                }
                else
                {
                    UdpSocket.UpdateRemoteEndPoint(remoteEndPoint);
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
            _receiveBuffer.Write(buffer.AsSpan(offset, length));

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
            UdpSocket.Close(reason);
        }

        public virtual void Close()
        {
            Close(ECloseReason.ServerClosing);
        }      
    }
}
