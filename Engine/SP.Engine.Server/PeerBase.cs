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
using SP.Engine.Runtime.Protocol;
using SP.Engine.Runtime.Security;

namespace SP.Engine.Server;

public enum PeerKind : byte
{
    None = 0,
    User,
    Server
}

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
    bool Send(IProtocolData data);
    void Close(CloseReason reason);
}

public abstract class PeerBase : IPeer, IDisposable
{
    private DiffieHellman _diffieHellman;
    private Lz4Compressor _compressor;
    private AesGcmEncryptor _encryptor;
    private Session _session;
    private readonly ReliableMessageProcessor _messageProcessor;
    private int _stateCode = PeerStateConst.NotAuthenticated;
    private uint _lastSentAck;
    private DateTime _lastAckTime;
    private bool _disposed;
    private readonly List<TcpMessage> _retriesCache = [];
    
    protected PeerBase(PeerKind kind, Session session)
    {
        PeerId = PeerIdGenerator.Generate();
        Kind = kind;
        Logger = session.Logger;
        _session = session;
        _session.Peer = this;
        
        var config = session.Config.Network;
        _compressor = new Lz4Compressor(config.MaxPayloadLength);

        _messageProcessor = ReliableMessageProcessor.CreateBuilder()
            .SetRetransmitPolicy(config.ReliableMaxRetransmitCount, config.ReliableInitialRetransmitTimeoutMs)
            .SetAckPolicy(config.ReliableMaxAckDelayMs, config.ReliableAckFrequency)
            .SetMaxOutOfOrderCount(config.ReliableMaxOutOfOrderCount)
            .SetPendingQueueCapacity(config.ReliablePendingQueueCapacity)
            .SetInFlightLimit(config.ReliableInFlightLimit)
            .Build();
    }

    protected PeerBase(PeerBase other)
    {
        PeerId = other.PeerId;
        other.PeerId = 0;
        
        Kind = other.Kind;
        Logger = other.Logger;
        _stateCode = Interlocked.CompareExchange(ref other._stateCode, PeerStateConst.Closed, other._stateCode);
        
        _diffieHellman = other._diffieHellman;
        other._diffieHellman = null;
        
        _encryptor = other._encryptor;
        other._encryptor = null;
        
        _compressor = other._compressor;
        other._compressor = null;
        
        _messageProcessor = other._messageProcessor;
        _session = other._session;
        _session.Peer = this;
    }

    public double AvgRTTMs { get; private set; }
    public double LatencyJitterMs { get; private set; }
    public IPEndPoint LocalEndPoint => _session.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => _session.RemoteEndPoint;
    public bool IsConnected => _stateCode is PeerStateConst.Authenticated or PeerStateConst.Online;
    public bool IsClosed => _stateCode is PeerStateConst.Closed;

    internal byte[] LocalPublicKey => _diffieHellman?.PublicKey;

    IEncryptor ICommandContext.Encryptor => _encryptor;
    ICompressor ICommandContext.Compressor => _compressor;
    
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
        if (IsClosed) return false;

        var policy = _session.PolicySnapshot.Resolve(data.Id);
        var encryptor = policy.UseEncrypt ? _encryptor : null;
        var compressor = policy.UseCompress ? _compressor : null;
        var originalChannel = data.Channel;
        var channel = originalChannel == ChannelKind.Unreliable && !_session.IsUdpAvailable
            ? ChannelKind.Reliable
            : originalChannel;
        
        switch (channel)
        {
            case ChannelKind.Reliable:
            {
                var tcp = new TcpMessage();
                tcp.Serialize(data, policy, encryptor, compressor);  

                using (tcp)
                {
                    if (originalChannel == ChannelKind.Unreliable)
                    {
                        return _session.Send(channel, tcp);   
                    }

                    if (IsConnected)
                    {
                        if (!_messageProcessor.PrepareReliableSend(tcp))
                        {
                            _messageProcessor.EnqueuePendingMessage(tcp);
                            return true;
                        }

                        if (!_session.Send(channel, tcp)) return false;  
                    }
                    else
                    {
                        _messageProcessor.EnqueuePendingMessage(tcp);
                    }
                }
                
                return true;
            }
            case ChannelKind.Unreliable:
            {
                if (!IsConnected) return false;
                
                var udp = new UdpMessage();
                udp.Serialize(data, policy, encryptor, compressor);  
                
                using (udp)
                {
                    return _session.Send(channel, udp);   
                }
            }
            default:
                throw new Exception($"Unknown channel: {channel}");
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
            _messageProcessor.Dispose();
        }

        _disposed = true;
    }

    public virtual void Tick()
    {
        if (!IsConnected) return;
        CheckAndFlushPeriodAck();
        ProcessRetransmission();
        FlushPendingMessages();   
    }

    private void ProcessRetransmission()
    {
        _retriesCache.Clear();
        
        try
        {
            var failed = _messageProcessor.ExtractRetransmissions(_retriesCache);
            if (failed != null)
            {
                Logger.Warn("Retransmission exhausted. PeerId: {0}, Failed Seq: {1}, ProtocolId: {2}",
                    PeerId, failed.SequenceNumber, failed.Id);
            
                Close(CloseReason.LimitExceededRetransmission);
                return;
            }

            foreach (var message in _retriesCache)
            {
                _session.Send(ChannelKind.Reliable, message);
            }
        }
        catch (ObjectDisposedException)
        {

        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error: {0}\nStacktrace: {1}", ex.Message, ex.StackTrace);
        }
    }

    private void CheckAndFlushPeriodAck()
    {
        var ackNumber = _messageProcessor.NextExpectedSeq;
        if (ackNumber <= _lastSentAck) return;
        
        var nowUtc = DateTime.UtcNow;
        var elapsedMs = (nowUtc - _lastAckTime).TotalMilliseconds;
        var pendingCount = ackNumber - _lastSentAck;

        if (elapsedMs < _messageProcessor.MaxAckDelayMs && pendingCount < _messageProcessor.AckFrequency)
            return;

        _lastAckTime = DateTime.UtcNow;
        _lastSentAck = ackNumber;
        _session.SendMessageAck(ackNumber);
    }

    internal void RecordPingData(double rttMs, double avgRttMs, double jitterMs)
    {
        _messageProcessor.AddRtoSample(rttMs);
        AvgRTTMs = avgRttMs;
        LatencyJitterMs = jitterMs;
    }

    internal void HandleRemoteAck(uint remoteAckNumber)
    {
        _messageProcessor.AcknowledgeInFlight(remoteAckNumber);   
    }

    internal List<TcpMessage> IngestReceivedMessage(TcpMessage message)
    {
        return _messageProcessor.IngestReceivedMessage(message);
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
        _messageProcessor.ResetInFlightMessages();
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
        _messageProcessor.Dispose();
        _diffieHellman?.Dispose();
        _session.Peer = null;
        OnLeaveServer(reason);
    }
    
    private void FlushPendingMessages()
    {
        var messages = _messageProcessor.FlushPendingMessages();
        if (messages.Count == 0) return;
        
        var processed = 0;
        foreach (var message in messages)
        {
            if (!IsConnected) break;
            if (!_messageProcessor.PrepareReliableSend(message)) break;
            _session.Send(ChannelKind.Reliable, message);
            
            message.Dispose();
            processed++;
        }
        
        if (processed >= messages.Count) return;

        for (var i = processed; i < messages.Count; i++)
        {
            _messageProcessor.EnqueuePendingMessage(messages[i]);
            messages[i].Dispose();
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
