using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using SP.Common.Buffer;
using SP.Common.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Networking;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.ProtocolHandler;

namespace SP.Engine.Server
{
    public interface IClientSession : ILogContext, IHandleContext
    {
        string SessionId { get; }
        IPEndPoint LocalEndPoint { get; }
        IPEndPoint RemoteEndPoint { get; }
        EngineConfig Config { get; }
        IEngine Engine { get; }
        TcpNetworkSession TcpSession { get; }

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
        public EngineConfig Config => Engine.Config;
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
            _receiveBuffer = new BinaryBuffer(engine.Config.Network.ReceiveBufferSize);
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
                    if (!UdpSocket?.TrySend(udpMessage) ?? false)
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
                if (_receiveBuffer.AvailableBytes < 1024)
                    _receiveBuffer.Trim();
            }
        }
        
        private IEnumerable<TcpMessage> Filter()
        {
            const int maxFramePerTick = 128;
            var produced = 0;
            while (produced < maxFramePerTick)
            {
                if (_receiveBuffer.AvailableBytes < TcpHeader.HeaderSize)
                    yield break;
                
                var headerSpan = _receiveBuffer.Peek(TcpHeader.HeaderSize);
                var result = TcpHeader.TryParse(headerSpan, out var header);
                
                switch (result)
                {
                    case TcpHeader.ParseResult.Success:
                    {
                        long frameLen = header.Length + header.PayloadLength;
                        if (frameLen <= 0 || frameLen > Config.Network.MaxFrameBytes)
                        {
                            Logger.Warn("Frame too large/small. max={0}, got={1}, (protocolId={2})", 
                                Config.Network.MaxFrameBytes, frameLen, header.ProtocolId);
                            Close(ECloseReason.ProtocolError);
                            yield break;
                        }

                        var len = (int)frameLen;
                        if (_receiveBuffer.AvailableBytes < len)
                            yield break;
                
                        var frameBytes = _receiveBuffer.ReadBytes(len);
                        yield return new TcpMessage(header, new ArraySegment<byte>(frameBytes));
                        produced++;
                        break;
                    }
                    case TcpHeader.ParseResult.Invalid:
                        Close(ECloseReason.ProtocolError);
                        yield break;
                    case TcpHeader.ParseResult.NeedMore:
                        yield break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
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
            UdpSocket?.Close(reason);
        }

        public virtual void Close()
        {
            Close(ECloseReason.ServerClosing);
        }      
    }
}
