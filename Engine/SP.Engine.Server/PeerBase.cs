using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using SP.Core.Logging;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Channel;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Compression;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Security;
using SP.Engine.Server.Protocol;

namespace SP.Engine.Server;


public enum PeerState
{
    NotAuthenticated = PeerStateConst.NotAuthenticated,
    Authenticated = PeerStateConst.Authenticated,
    Online = PeerStateConst.Online,
    Offline = PeerStateConst.Offline,
    Closed = PeerStateConst.Closed
}

internal static class PeerStateConst
{
    public const int NotAuthenticated = 0;
    public const int Authenticated = 1;
    public const int Online = 2;
    public const int Offline = 3;
    public const int Closed = 4;
}

public interface IPeer : ICommandContext
{
    uint PeerId { get; }
    PeerKind Kind { get; }
    PeerState State { get; }
    bool Send<T>(T data) where T : class, IProtocolData, new();
    void Close(CloseReason reason);
}

public abstract class PeerBase : IPeer, IDisposable
{
    private readonly Lz4Compressor _compressor;
    private DiffieHellman _diffieHellman;
    private AesGcmEncryptor _encryptor;
    private Session _session;
    private int _stateCode = PeerStateConst.NotAuthenticated;
    private uint _lastSentAck = 1;
    private DateTime _lastAckTime;
    private bool _disposed;
    private readonly List<TcpMessage> _retriesCache = [];
    private int _isSending;
    private readonly ConcurrentQueue<IProtocolData> _sendQueue = new();
    
    internal ReliableMessageProcessor MessageProcessor { get; }

    public EngineBase Engine => _session.Engine;
    public double AvgRttMs { get; private set; }
    public double JitterMs { get; private set; }
    public IPEndPoint LocalEndPoint => _session.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => _session.RemoteEndPoint;
    public bool IsConnected => _stateCode is PeerStateConst.Authenticated or PeerStateConst.Online;
    public bool IsClosed => _stateCode is PeerStateConst.Closed;

    internal byte[] LocalPublicKey => _diffieHellman?.PublicKey;

    IEncryptor ICommandContext.Encryptor => _encryptor;
    ICompressor ICommandContext.Compressor => _compressor;
    
    protected PeerBase(PeerKind kind, Session session)
    {
        PeerId = PeerIdGenerator.Generate();
        Kind = kind;
        Logger = session.Logger;
        _session = session;
        _session.Peer = this;
        
        var config = session.Config.Network;
        _compressor = new Lz4Compressor(config.MaxPayloadLength);

        MessageProcessor = ReliableMessageProcessor.CreateBuilder()
            .SetRetransmitPolicy(config.ReliableMaxRetransmitCount, config.ReliableInitialRetransmitTimeoutMs)
            .SetAckPolicy(config.ReliableMaxAckDelayMs, config.ReliableAckFrequency)
            .SetMaxOutOfOrderCount(config.ReliableMaxOutOfOrderCount)
            .SetPendingQueueCapacity(config.ReliablePendingQueueCapacity)
            .SetInFlightLimit(config.ReliableInFlightLimit)
            .Build();
    }
    
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    public PeerState State => (PeerState)_stateCode;
    public uint PeerId { get; private set; }
    public PeerKind Kind { get; }
    public ILogger Logger { get; }

    public void Close(CloseReason reason)
    {
        if (IsClosed) return;
        _session.Close(reason);
    }

    public bool Send(IProtocolData data)
    {
        if (data == null) return false;
        return !IsClosed && ProtocolDispatcher.DispatchSend(this, data);
    }
    
    public bool Send<T>(T data) where T : class, IProtocolData, new()
    {
        if (IsClosed) return false;
        
        _sendQueue.Enqueue(data);

        if (Interlocked.CompareExchange(ref _isSending, 1, 0) == 0)
        {
            ThreadPool.UnsafeQueueUserWorkItem(ProcessSendQueue, this);
        }
        
        return true;
    }

    private static void ProcessSendQueue(object state)
    {
        var peer = (PeerBase)state;
        
        try
        {
            while (peer._sendQueue.TryDequeue(out var data))
            {
                if (peer.IsClosed) return;
                ExecuteSend(peer, data);
            }
        }
        finally
        {
            Interlocked.Exchange(ref peer._isSending, 0);

            if (!peer._sendQueue.IsEmpty && Interlocked.CompareExchange(ref peer._isSending, 1, 0) == 0)
            {
                ThreadPool.UnsafeQueueUserWorkItem(ProcessSendQueue, peer);
            }
        }
    }

    private static void ExecuteSend(PeerBase peer, IProtocolData data)
    {
        IMessage message = null;
        
        try
        {
            var session = peer._session;
            var policy = session.PolicySnapshot.Resolve(data.Id);
            var encryptor = policy.UseEncrypt ? peer._encryptor : null;
            var compressor = policy.UseCompress ? peer._compressor : null;

            var channel = data.Channel;
            if (channel == ChannelKind.Reliable || (channel == ChannelKind.Unreliable && !session.IsUdpAvailable))
            {
                message = MessagePool<TcpMessage>.Rent();
                message.Serialize(data, policy, encryptor, compressor);

                if (channel == ChannelKind.Unreliable)
                {
                    session.TrySend(ChannelKind.Reliable, message);
                    return;
                }

                if (peer.IsConnected)
                {
                    if (peer.MessageProcessor.RegisterInFlight((TcpMessage)message, out var inFlightMessage))
                    {
                        session.TrySend(channel, inFlightMessage);
                        return;
                    }
                }
                
                peer.MessageProcessor.EnqueuePendingMessage((TcpMessage)message);
            }
            else
            {
                if (!peer.IsConnected) return;
                
                message = MessagePool<UdpMessage>.Rent();
                message.Serialize(data, policy, encryptor, compressor);
                session.TrySend(channel, message);
            }
        }
        catch (Exception ex)
        {
            peer.Logger.Error(ex, "Peer {0} execute send failed. P: {1}", peer.PeerId, data.Id);
        }
        finally
        {
            message?.Dispose();
        }
    }

    ~PeerBase()
    {
        Dispose(false);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            if (PeerId != 0)
                PeerIdGenerator.Free(PeerId);
            
            _diffieHellman?.Dispose();
            MessageProcessor.Dispose();
        }

        _disposed = true;
    }

    public virtual void Tick()
    {
        CheckAndFlushPeriodAck();
        ProcessRetransmission();
        FlushPendingMessages();   
    }

    private void ProcessRetransmission()
    {
        if (!IsConnected || _session.IsClosing || _session.IsClosed) return;
        
        _retriesCache.Clear();
        
        try
        {
            var failed = MessageProcessor.PrepareRetransmissions(_retriesCache);
            if (failed != null)
            {
                Logger.Warn("Retransmission exhausted. PeerId: {0}, Failed Seq: {1}, ProtocolId: {2}",
                    PeerId, failed.SequenceNumber, failed.Id);
            
                Close(CloseReason.LimitExceededRetransmission);
                return;
            }

            foreach (var message in _retriesCache)
            {
                using (message) _session.TrySend(ChannelKind.Reliable, message);
            }
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error: {0}\nStacktrace: {1}", ex.Message, ex.StackTrace);
        }
    }

    private void CheckAndFlushPeriodAck()
    {
        if (!IsConnected) return;
        
        var expectedSeq = MessageProcessor.NextExpectedSeq;
        if (expectedSeq <= _lastSentAck) return;
        
        var nowUtc = DateTime.UtcNow;
        var elapsedMs = (nowUtc - _lastAckTime).TotalMilliseconds;
        var pendingCount = expectedSeq - _lastSentAck;

        if (elapsedMs < MessageProcessor.MaxAckDelayMs && pendingCount < MessageProcessor.AckFrequency)
            return;

        _lastAckTime = nowUtc;
        _lastSentAck = expectedSeq;
        _session.SendMessageAck(expectedSeq);
    }

    internal void InheritSecurityContext(PeerBase sourcePeer)
    {
        if (sourcePeer == null) return;

        _diffieHellman = sourcePeer._diffieHellman;
        _encryptor = sourcePeer._encryptor;

        sourcePeer._diffieHellman = null;
        sourcePeer._encryptor = null;
    }

    internal void RecordPingData(double rttMs, double avgRttMs, double jitterMs)
    {
        MessageProcessor.AddRtoSample(rttMs);
        AvgRttMs = avgRttMs;
        JitterMs = jitterMs;
    }

    internal void HandleRemoteAck(uint nextExpectedSeq)
    {
        MessageProcessor.AcknowledgeInFlight(nextExpectedSeq);   
    }

    internal ReceiveIngestResult ReceiveIngestMessage(TcpMessage message, List<TcpMessage> destinationList)
    {
        return MessageProcessor.ReceiveIngestMessage(message, destinationList);
    }

    internal void JoinServer()
    {
        if (Interlocked.CompareExchange(ref _stateCode, PeerStateConst.Authenticated, PeerStateConst.NotAuthenticated)
            != PeerStateConst.NotAuthenticated)
        {
            Logger.Error("The peer has joined the server.");
            return;
        }

        OnJoinServer();
    }
    
    internal void Offline(CloseReason reason)
    {
        Interlocked.Exchange(ref _stateCode, PeerStateConst.Offline);
        MessageProcessor.ResetInFlightMessages();
        OnOffline(reason);
    }
    
    internal void Online(Session session)
    {
        Interlocked.Exchange(ref _stateCode, PeerStateConst.Online);
        _session = session;
        OnOnline();
    }

    internal void LeaveServer(CloseReason reason)
    {
        Interlocked.Exchange(ref _stateCode, PeerStateConst.Closed);
        PeerIdGenerator.Free(PeerId);
        PeerId = 0;
        MessageProcessor.Dispose();
        _diffieHellman?.Dispose();
        _session.Peer = null;
        OnLeaveServer(reason);
    }
    
    private void FlushPendingMessages()
    {
        if (!IsConnected) return;

        while (MessageProcessor.TryPeekPendingMessage(out var message))
        {
            if (!MessageProcessor.RegisterInFlight(message, out var inFlightMessage)) break;
            MessageProcessor.DequeuePendingMessage();
            _session.TrySend(ChannelKind.Reliable, inFlightMessage);
            message.Dispose();
        }
    }

    internal bool TryKeyExchange(DhKeySize keySize, byte[] peerPublicKey)
    {
        if (peerPublicKey == null || peerPublicKey.Length == 0)
        {
            Logger?.Warn("Key exchange validation failed: peer public key is empty.");
            return false;
        }

        byte[] sharedKey = null;

        try
        {
            _diffieHellman = new DiffieHellman(keySize);
            sharedKey = _diffieHellman.DeriveSharedKey(peerPublicKey);
            _encryptor = new AesGcmEncryptor(sharedKey);
            return true;
        }
        catch (ArgumentException ex)
        {
            Logger?.Warn("Key exchange validation failed: {0}", ex.Message);
            return false;
        }
        catch (CryptographicException ex)
        {
            Logger?.Error("Key exchange provider error: {0}", ex.Message);
            return false;
        }
        catch (Exception ex)
        {
            Logger?.Error(ex, "Key exchange unexpected error.");
            return false;
        }
        finally
        {
            if (sharedKey != null)
                CryptographicOperations.ZeroMemory(sharedKey);
        }
    }

    protected virtual void OnJoinServer()
    {
    }

    protected virtual void OnLeaveServer(CloseReason reason)
    {
    }

    protected virtual void OnOnline()
    {
    }

    protected virtual void OnOffline(CloseReason reason)
    {
    }

    public override string ToString()
    {
        return $"sessionId={_session.SessionId}, peerId={PeerId}, peerType={Kind}, remoteEndPoint={RemoteEndPoint}";
    }

    private static class PeerIdGenerator
    {
        private static readonly ConcurrentQueue<uint> FreePeerIdPool = new();
        private static int _latestPeerId;

        public static uint Generate()
        {
            if (FreePeerIdPool.TryDequeue(out var peerId))
            {
                return peerId;
            }

            var newId = (uint)Interlocked.Increment(ref _latestPeerId);
            if (newId == 0)
            {
                throw new InvalidOperationException("Peer ID pool exhausted.");
            }
            
            return newId;
        }

        public static void Free(uint peerId)
        {
            if (peerId == 0) return;
            FreePeerIdPool.Enqueue(peerId);
        }
    }
}
