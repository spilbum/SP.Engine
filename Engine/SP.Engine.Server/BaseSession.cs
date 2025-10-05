using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using SP.Common.Buffer;
using SP.Common.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Security;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.ProtocolHandler;

namespace SP.Engine.Server
{
    public interface IBaseSession : ILogContext, IHandleContext
    {
        IBaseEngine Engine { get; }
        INetworkSession NetworkSession { get; }
        IEngineConfig Config { get; }
        IEncryptor Encryptor { get; }
        ICompressor Compressor { get; }
        void ProcessBuffer(byte[] buffer, int offset, int length);
        void ProcessDatagram(ArraySegment<byte> buffer, UdpHeader header, Socket socket, IPEndPoint remoteEndPoint);
    }
    
    public abstract class BaseSession<TSession> : IBaseSession
        where TSession : BaseSession<TSession>, IBaseSession, new()
    {
        private BinaryBuffer _receiveBuffer;
        private BaseEngine<TSession> _engine;
        private readonly ChannelRouter _channelRouter = new();
        private readonly UdpFragmentAssembler _fragmentAssembler = new();

        IBaseEngine IBaseSession.Engine => _engine;
        public IEngineConfig Config => _engine.Config;
        public ILogger Logger => _engine.Logger;
        public IPEndPoint LocalEndPoint => NetworkSession.LocalEndPoint;
        public IPEndPoint RemoteEndPoint => NetworkSession.RemoteEndPoint;
        public DateTime StartTime { get; }
        public DateTime LastActiveTime { get; private set; }
        public string Id { get; private set; }
        public DateTime StartClosingTime { get; protected set; }
        public bool IsAuthorized { get; protected set; }
        public INetworkSession NetworkSession { get; private set; }
        public UdpSocket UdpSocket { get; private set; }
        public CloseReason CloseReason { get; private set; }
        public IEncryptor Encryptor { get; protected set; }
        public ICompressor Compressor { get; protected set; }
        public bool IsConnected { get; internal set; }
        public bool IsClosed { get; internal set; }

        protected BaseSession()
        {
            StartTime = DateTime.UtcNow;
            LastActiveTime = StartTime;
            Compressor = new Lz4Compressor();
        }

        public virtual void Initialize(IBaseEngine engine, TcpNetworkSession networkSession)
        {
            _engine = (BaseEngine<TSession>)engine;
            NetworkSession = networkSession;
            Id = networkSession.SessionId;
            _receiveBuffer = new BinaryBuffer(engine.Config.Network.ReceiveBufferSize);
            networkSession.Attach(this);
            _channelRouter.Bind(new ReliableChannel(networkSession));
            //_engine.Scheduler.Schedule(_fragmentAssembler.Cleanup, TimeSpan.FromMinutes(15), 5000, 5000);
            IsConnected = true;
        }

        public bool TrySend(ChannelKind channel, IMessage message)
        {
            switch (channel)
            {
                case ChannelKind.Reliable when message is not TcpMessage:
                case ChannelKind.Unreliable when message is not UdpMessage:
                    return false;
                default:
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

        void IBaseSession.ProcessDatagram(ArraySegment<byte> datagram, UdpHeader header, Socket socket, IPEndPoint remoteEndPoint)
        {
            EnsureUdpSocket(socket, remoteEndPoint);

            var bodyOffset = datagram.Offset + header.Size;
            var bodyLength = header.PayloadLength;
            var bodySpan = new ReadOnlySpan<byte>(datagram.Array, bodyOffset, bodyLength);
            
            if (header.Flags.HasFlag(HeaderFlags.Fragment))
            {
                if (bodyLength < UdpFragmentHeader.ByteSize) return;
                
                if (!UdpFragmentHeader.TryRead(bodySpan[..UdpFragmentHeader.ByteSize], out var fragHeader))
                    return;
                
                if (bodyLength < UdpFragmentHeader.ByteSize + fragHeader.PayloadLength)
                    return;

                var fragPayload = new ArraySegment<byte>(
                    datagram.Array!, bodyOffset + UdpFragmentHeader.ByteSize, fragHeader.PayloadLength);
                
                
            }
            else
            {
                var payload = new ArraySegment<byte>(datagram.Array!, bodyOffset, bodyLength);
                var msg = new UdpMessage(header, payload);
                ProcessMessage(msg);
            }
        }

        void IBaseSession.ProcessBuffer(byte[] buffer, int offset, int length)
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
                _receiveBuffer.MaybeTrim(TcpHeader.ByteSize);
            }
        }
        
        private IEnumerable<TcpMessage> Filter()
        {
            const int maxFramePerTick = 128;
            var produced = 0;
            while (produced < maxFramePerTick)
            {
                if (_receiveBuffer.ReadableBytes < TcpHeader.ByteSize)
                    yield break;
                
                var headerSpan = _receiveBuffer.Read(TcpHeader.ByteSize);
                if (!TcpHeader.TryParse(headerSpan, out var header, out var consumed))
                    yield break;
                
                long frameLen = consumed + header.PayloadLength;
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
            }
        }

        protected abstract void ExecuteMessage(IMessage message);

        public virtual void Close(CloseReason reason)
        {
            _channelRouter.Unbind(ChannelKind.Reliable);
            _channelRouter.Unbind(ChannelKind.Unreliable);
            NetworkSession.Close(reason);
            UdpSocket?.Close(reason);
            CloseReason = reason;
        }

        public virtual void Close()
        {
            Close(CloseReason.ServerClosing);
        }      
    }
}
