using System;
using System.Buffers;
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
        void ProcessDatagram(byte[] datagram, UdpHeader header, Socket socket, IPEndPoint remoteEndPoint);
    }
    
    public abstract class BaseSession<TSession> : IBaseSession
        where TSession : BaseSession<TSession>, IBaseSession, new()
    {
        private BinaryBuffer _receiveBuffer;
        private BaseEngine<TSession> _engine;
        private IDisposable _assemblerCleanupScheduler;
        private readonly ChannelRouter _channelRouter = new();

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
        
        private void OnReceivedMessage(IMessage message)
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

        private void StartFragmentAssemblerCleanupScheduler()
        {
            var timeoutSec = Config.Session.FragmentAssemblerCleanupTimeoutSec;
            var intervalMs = timeoutSec / 2 * 1000;
            _assemblerCleanupScheduler = _engine.Scheduler.Schedule(() =>
                UdpSocket.Assembler.Cleanup(TimeSpan.FromSeconds(timeoutSec)), intervalMs, intervalMs);
        }

        private void StopFragmentAssemblerCleanupScheduler()
        {
            _assemblerCleanupScheduler?.Dispose();
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
                    StartFragmentAssemblerCleanupScheduler();
                }
                else
                {
                    UdpSocket.UpdateRemoteEndPoint(remoteEndPoint);
                }   
            }
        }

        void IBaseSession.ProcessDatagram(byte[] datagram, UdpHeader header, Socket socket, IPEndPoint remoteEndPoint)
        {
            EnsureUdpSocket(socket, remoteEndPoint);

            var bodyOffset = header.Size;
            var bodyLen = header.PayloadLength;
            var bodySpan = new ReadOnlySpan<byte>(datagram, bodyOffset, bodyLen);

            if (header.Flags.HasFlag(HeaderFlags.Fragment))
            {
                if (!UdpFragmentHeader.TryParse(bodySpan, out var fragHeader, out var consumed))
                    return;

                if (bodyLen < consumed + fragHeader.FragmentLength)
                    return;

                var fragPayload = new ArraySegment<byte>(
                    datagram, bodyOffset + consumed, fragHeader.FragmentLength);

                if (!UdpSocket.Assembler.TryAssemble(header, fragHeader, fragPayload, out var assembled))
                    return;

                var normalizedHeader = new UdpHeaderBuilder()
                    .From(header)
                    .WithPayloadLength(assembled.Count)
                    .Build();

                var msg = new UdpMessage(normalizedHeader, assembled);
                OnReceivedMessage(msg);
            }
            else
            {
                var payload = new ArraySegment<byte>(datagram, bodyOffset, bodyLen);
                var msg = new UdpMessage(header, payload);
                OnReceivedMessage(msg);
            }
        }

        void IBaseSession.ProcessBuffer(byte[] buffer, int offset, int length)
        {
            var span = new ReadOnlySpan<byte>(buffer, offset, length);
            _receiveBuffer.WriteSpan(span);
            
            try
            {
                foreach (var message in Filter())
                    OnReceivedMessage(message);
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
            const int headerSize = TcpHeader.ByteSize;
            var produced = 0;
            while (produced < maxFramePerTick)
            {
                if (_receiveBuffer.ReadableBytes < headerSize)
                    yield break;

                var headerSpan = _receiveBuffer.PeekSpan(headerSize);
                if (!TcpHeader.TryParse(headerSpan, out var header, out var consumed))
                    yield break;
                
                var bodyLen = header.PayloadLength;
                if (bodyLen <= 0 || bodyLen > Config.Network.MaxFrameBytes)
                {
                    Logger.Warn("Too large or small. id={0}, maxFrameBytes={1}, bodyLen={2}", 
                        header.Id, Config.Network.MaxFrameBytes, bodyLen);
                    Close(CloseReason.ProtocolError);
                    yield break;
                }

                if (_receiveBuffer.ReadableBytes < bodyLen)
                    yield break;
                
                _receiveBuffer.Advance(consumed);
                
                var payload = _receiveBuffer.ReadBytes(bodyLen);
                yield return new TcpMessage(header, new ArraySegment<byte>(payload));
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
            StopFragmentAssemblerCleanupScheduler();
            CloseReason = reason;
        }

        public virtual void Close()
        {
            Close(CloseReason.ServerClosing);
        }      
    }
}
