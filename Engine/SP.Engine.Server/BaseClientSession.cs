using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using SP.Common.Buffer;
using SP.Common.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Security;
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
        TcpNetworkSession Session { get; }
        IEncryptor Encryptor { get; }

        bool Send(ChannelKind channel, IMessage message);
        void ProcessBuffer(byte[] buffer, int offset, int length);
        void ProcessBuffer(byte[] buffer, UdpHeader header, Socket socket, IPEndPoint remoteEndPoint);
        void Close(CloseReason reason);
    }

    public abstract class BaseClientSession<TSession> : IClientSession
        where TSession : BaseClientSession<TSession>, IClientSession, new()
    {
        private BinaryBuffer _receiveBuffer;
        private BaseEngine<TSession> _engine;
        private readonly ChannelRouter _channelRouter = new();
        private readonly UdpFragmentAssembler _fragmentAssembler = new();

        IEngine IClientSession.Engine => _engine;
        public EngineConfig Config => _engine.Config;
        public ILogger Logger => _engine.Logger;
        public IPEndPoint LocalEndPoint => Session.LocalEndPoint;
        public IPEndPoint RemoteEndPoint => Session.RemoteEndPoint;
        public bool IsConnected { get; internal set; }
        public DateTime StartTime { get; }
        public DateTime LastActiveTime { get; private set; }
        public string SessionId { get; private set; }
        public DateTime StartClosingTime { get; protected set; }
        public bool IsAuthorized { get; protected set; }
        public TcpNetworkSession Session { get; private set; }
        public UdpSocket UdpSocket { get; private set; }
        public CloseReason CloseReason { get; private set; }
        public IEncryptor Encryptor { get; protected set; }

        protected BaseClientSession()
        {
            StartTime = DateTime.UtcNow;
            LastActiveTime = StartTime;
        }

        public virtual void Initialize(IEngine engine, TcpNetworkSession networkSession)
        {
            _engine = (BaseEngine<TSession>)engine;
            Session = networkSession;
            SessionId = networkSession.SessionId;
            _receiveBuffer = new BinaryBuffer(engine.Config.Network.ReceiveBufferSize);
            networkSession.Attach(this);
            _channelRouter.Bind(new ReliableChannel(networkSession));
            _engine.Scheduler.Schedule(_fragmentAssembler.Cleanup, TimeSpan.FromMinutes(15), 5000, 5000);
            IsConnected = true;
        }

        public bool Send(ChannelKind channel, IMessage message)
        {
            switch (channel)
            {
                case ChannelKind.Reliable when message is not TcpMessage:
                case ChannelKind.Unreliable when message is not UdpMessage:
                    return false;
                default:
                    if (!IsConnected) return false;
                    if (!_channelRouter.TrySend(channel, message)) return false;
                    LastActiveTime = DateTime.UtcNow;
                    return true;
            }
        }
        
        private void ProcessMessage(IMessage message)
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
        
        private void EnsureUdpSocket(Socket socket, IPEndPoint remoteEndPoint)
        {
            lock (this)
            {
                if (UdpSocket == null)
                {
                    UdpSocket = new UdpSocket(socket, remoteEndPoint);
                    UdpSocket.Attach(this);
                    _channelRouter.Bind(new UnreliableChannel(UdpSocket));
                }
                else
                {
                    UdpSocket.UpdateRemoteEndPoint(remoteEndPoint);
                }   
            }
        }

        void IClientSession.ProcessBuffer(byte[] buffer, UdpHeader header, Socket socket, IPEndPoint remoteEndPoint)
        {
            EnsureUdpSocket(socket, remoteEndPoint);

            if (header.IsFragmentation)
            {
                if (!UdpFragment.TryParse(buffer, out var fragment))
                {
                    Logger.Warn("Failed to parse fragmentation data. datagram={0}", buffer.Length);
                    return;
                }

                if (!_fragmentAssembler.TryAssemble(fragment, out var payload))
                    return;

                var msg = new UdpMessage(header, payload);
                ProcessMessage(msg);
            }
            else
            {
                var payload = new ArraySegment<byte>(buffer);
                var msg = new UdpMessage(header, payload);
                ProcessMessage(msg);
            }
        }

        void IClientSession.ProcessBuffer(byte[] buffer, int offset, int length)
        {
            var span = buffer.AsSpan(offset, length);
            _receiveBuffer.Write(span);
            
            try
            {
                foreach (var msg in Filter())
                    ProcessMessage(msg);
            }
            catch (Exception e)
            {
                Logger.Error(e);
            }
            finally
            {  
                _receiveBuffer.MaybeTrim(TcpHeader.HeaderSize);
            }
        }
        
        private IEnumerable<TcpMessage> Filter()
        {
            const int maxFramePerTick = 128;
            var produced = 0;
            while (produced < maxFramePerTick)
            {
                if (_receiveBuffer.ReadableBytes < TcpHeader.HeaderSize)
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
                            Logger.Warn("Frame too large/small. max={0}, got={1}, (id={2})", 
                                Config.Network.MaxFrameBytes, frameLen, header.Id);
                            Close(CloseReason.ProtocolError);
                            yield break;
                        }

                        var len = (int)frameLen;
                        if (_receiveBuffer.ReadableBytes < len)
                            yield break;
                
                        var frameBytes = _receiveBuffer.ReadBytes(len);
                        yield return new TcpMessage(header, new ArraySegment<byte>(frameBytes));
                        produced++;
                        break;
                    }
                    case TcpHeader.ParseResult.Invalid:
                        Close(CloseReason.ProtocolError);
                        yield break;
                    case TcpHeader.ParseResult.NeedMore:
                        yield break;
                    default:
                        throw new ArgumentOutOfRangeException();
                }
            }
        }

        protected abstract void ExecuteMessage(IMessage message);

        public virtual void Close(CloseReason reason)
        {
            _channelRouter.Unbind(ChannelKind.Reliable);
            _channelRouter.Unbind(ChannelKind.Unreliable);
            Session.Close(reason);
            UdpSocket?.Close(reason);
            CloseReason = reason;
        }

        public virtual void Close()
        {
            Close(CloseReason.ServerClosing);
        }      
    }
}
