using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Threading;
using SP.Core;
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
    ISession Session { get; }
    bool Send(IProtocolData data);
    void Close(CloseReason reason);
}

public abstract class BasePeer : IPeer, IDisposable
{
    private DiffieHellman _diffieHellman;
    private Lz4Compressor _compressor;
    private AesGcmEncryptor _encryptor;
    private IProtocolPolicySnapshot _policySnapshot;
    private Session _session;
    private ReliableMessageProcessor _messageProcessor = new();
    private int _stateCode = PeerStateConst.NotAuthenticated;
    private uint _lastSentAck;
    private DateTime _lastAckTime;
    private readonly object _ackLock = new();
    private PeerFiber _fiber;
    private bool _disposed;
    private int _pendingJobCount;
    
    public int PendingJobCount => Volatile.Read(ref _pendingJobCount);
    
    protected BasePeer(PeerKind kind, ISession session)
    {
        PeerId = PeerIdGenerator.Generate();
        Kind = kind;
        Logger = session.Logger;
        _session = (Session)session;
        _session.Peer = this;
        
        var config = session.Config;
        _compressor = new Lz4Compressor(config.Network.MaxFrameBytes);
        _messageProcessor.SetSendTimeoutMs(config.Network.SendTimeoutMs);
        _messageProcessor.SetMaxRetransmissionCount(config.Network.MaxRetransmissionCount);
        _messageProcessor.SetMaxAckDelayMs(config.Network.MaxAckDelayMs);
        _messageProcessor.SetAckStepThreshold(config.Network.AckStepThreshold);
        _messageProcessor.SetMaxOutOfOrder(config.Network.MaxOutOfOderCount);
        _policySnapshot = ProtocolPolicyRegistry.CreateSnapshot(PolicyDefaults.FallbackGlobals);
    }

    protected BasePeer(BasePeer other)
    {
        PeerId = other.PeerId;
        other.PeerId = 0;
        
        Kind = other.Kind;
        Logger = other.Logger;
        _fiber = other._fiber;
        _stateCode = Interlocked.CompareExchange(ref other._stateCode, PeerStateConst.Closed, other._stateCode);
        
        _diffieHellman = other._diffieHellman;
        other._diffieHellman = null;
        
        _encryptor = other._encryptor;
        other._encryptor = null;
        
        _compressor = other._compressor;
        other._compressor = null;
        
        _policySnapshot = other._policySnapshot;
        _pendingJobCount = Interlocked.Exchange(ref other._pendingJobCount, 0);
        
        _messageProcessor = other._messageProcessor;
        other._messageProcessor = null;
        
        _session = (Session)other.Session;
        _session.Peer = this;
    }

    public double LatencyAvgMs { get; private set; }
    public double LatencyJitterMs { get; private set; }
    public IEncryptor Encryptor => _encryptor;
    public ICompressor Compressor => _compressor;
    public IPEndPoint LocalEndPoint => _session.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => _session.RemoteEndPoint;
    public bool IsConnected => _stateCode is PeerStateConst.Authenticated or PeerStateConst.Online;
    public bool IsClosed => _stateCode is PeerStateConst.Closed;

    internal byte[] LocalPublicKey => _diffieHellman?.PublicKey;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    public bool EnqueueJob<T1, T2>(Action<T1, T2> action, T1 s1, T2 s2)
    {
        if (_fiber == null) return false;
        
        var count = Interlocked.Increment(ref _pendingJobCount);
        if (count >= _session.Config.Session.PeerJobBackPressureThreshold && !_session.IsPaused)
        {
            _session.PauseReceive();
        }

        _fiber.EnqueueJob(this, action, s1, s2);
        return true;
    }

    private long _accumulatedDwellTicks;
    private long _accumulatedExecutionTicks;
    private int _completedJobCount;

    internal void OnJobFinished(double dwellTimeMs, double executionTimeMs)
    {
        Interlocked.Add(ref _accumulatedDwellTicks, (long)(dwellTimeMs * TimeSpan.TicksPerMillisecond));
        Interlocked.Add(ref _accumulatedExecutionTicks, (long)(executionTimeMs * TimeSpan.TicksPerMillisecond));
        Interlocked.Increment(ref _completedJobCount);
        
        var count = Interlocked.Decrement(ref _pendingJobCount);

        if (executionTimeMs > 50)
        {
            Logger.Warn("Slow Job Detected: PeerId={0}, Execution={1:F2}ms, Dwell={2:F2}ms",
                PeerId, executionTimeMs, dwellTimeMs);    
        }

        if (count <= _session.Config.Session.PeerJobBackPressureThreshold && _session.IsPaused)
        {
            _session.ResumeReceive();
        }
    }

    internal (double totalDwell, double totalExec, int count) ExtractMetrics()
    {
        var count = Interlocked.Exchange(ref _completedJobCount, 0);
        if (count == 0) return (0, 0, 0);
        
        var dwellTicks = Interlocked.Exchange(ref _accumulatedDwellTicks, 0);
        var executionTicks = Interlocked.Exchange(ref _accumulatedExecutionTicks, 0);

        return (
            (double)dwellTicks / TimeSpan.TicksPerMillisecond,
            (double)executionTicks / TimeSpan.TicksPerMillisecond,
            count
        );
    }

    public PeerState State => (PeerState)_stateCode;
    public uint PeerId { get; private set; }
    public PeerKind Kind { get; }
    public ILogger Logger { get; }
    public ISession Session => _session;

    TProtocol ICommandContext.Deserialize<TProtocol>(IMessage message)
        => message.Deserialize<TProtocol>(_encryptor, _compressor);
    
    public void Close(CloseReason reason)
    {
        if (IsClosed) return;
        _session.Close(reason);
    }

    internal void OnSessionClosed(Session session)
    {
        if (_session.SessionId != session.SessionId) return;
        _fiber?.RemovePeer(this);
    }

    public bool Send(IProtocolData data)
    {
        if (IsClosed) return false;
        
        var policy = _policySnapshot.Resolve(data.Id);
        var encryptor = policy.UseEncrypt ? Encryptor : null;
        var compressor = policy.UseCompress ? Compressor : null;
        var originalChannel = data.Channel;
        var channel = originalChannel == ChannelKind.Unreliable && !_session.IsUdpAvailable
            ? ChannelKind.Reliable
            : originalChannel;
        
        switch (channel)
        {
            case ChannelKind.Reliable:
            {
                var tcp = new TcpMessage();
                using (tcp)
                {
                    var sc_serialize = new SlowChecker(50, "Serialize", Logger);
                    using (sc_serialize)
                    {
                        tcp.Serialize(data, policy, encryptor, compressor);   
                    }

                    if (originalChannel == ChannelKind.Unreliable)
                        return _session.TrySend(channel, tcp);   

                    if (IsConnected)
                    {
                        var sc_processor = new SlowChecker(50, "PrepareReliableSend", Logger);
                        using (sc_processor)
                        {
                            _messageProcessor?.PrepareReliableSend(tcp);  
                        }
                  
                        if (!_session.TrySend(channel, tcp)) return false; 
                        Interlocked.Exchange(ref _lastSentAck, tcp.AckNumber);
                    }
                    else
                    {
                        _messageProcessor?.EnqueuePendingMessage(tcp);
                    }

                    return true;
                }
            }
            case ChannelKind.Unreliable:
            {
                var udp = new UdpMessage();
                using (udp)
                {
                    udp.SetSessionId(_session.SessionId);
                    udp.Serialize(data, policy, encryptor, compressor);
                    return _session.TrySend(channel, udp);      
                }
            }
            default:
                throw new Exception($"Unknown channel: {channel}");
        }
    }

    ~BasePeer()
    {
        Dispose(false);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed)
            return;

        if (disposing)
        {
            PeerIdGenerator.Free(PeerId);
            _diffieHellman?.Dispose();
            _messageProcessor?.Dispose();
        }

        _disposed = true;
    }

    public virtual void Tick()
    {
        if (!IsConnected) return;
        ProcessRetransmission();
        MaintainAckStatus();
    }

    private void ProcessRetransmission()
    {
        if (_messageProcessor == null) return;
        var (retries, failed) = _messageProcessor.ProcessRetransmissions();
        if (failed is { Count: > 0 })
        {
            Logger.Warn("Connection terminated due to message delivery failure. sessionId: {0}, peerId: {1}, first seq: {2}, count: {3}",
                _session.SessionId, PeerId, failed[0].SequenceNumber, failed.Count);
            
            foreach (var m in failed)
                m.Dispose();
            _messageProcessor.RestartInFlightMessages();
            
            Close(CloseReason.LimitExceededRetransmission);
            return;
        }

        if (retries != null)
        {
            foreach (var message in retries.Where(message => _session.TrySend(ChannelKind.Reliable, message)))
            {
                Interlocked.Exchange(ref _lastSentAck, message.AckNumber);
            }   
        }
    }

    private void MaintainAckStatus()
    {
        if (_messageProcessor == null) return;
        var ackNumber = _messageProcessor.LastReceivedSeq;
        if (ackNumber <= _lastSentAck) return;
        
        var now = DateTime.UtcNow;
        var elapsedMs = (now - _lastAckTime).TotalMilliseconds;
        var pendingCount = ackNumber - _lastSentAck;

        if (elapsedMs < _messageProcessor.MaxAckDelayMs && pendingCount < _messageProcessor.AckStepThreshold)
            return;

        lock (_ackLock)
        {
            if (ackNumber <= _lastSentAck) return;
            if (elapsedMs < _messageProcessor.MaxAckDelayMs && pendingCount < _messageProcessor.AckStepThreshold)
                return;

            _lastAckTime = DateTime.UtcNow;
            _lastSentAck = ackNumber;
            _session.SendMessageAck(ackNumber);
        }
    }

    internal void OnPing(ushort rttMs, ushort avgRttMs, ushort jitterMs)
    {
        _messageProcessor?.AddRtoSample(rttMs);
        LatencyAvgMs = avgRttMs;
        LatencyJitterMs = jitterMs;
    }

    internal void OnMessageAck(uint ackNumber)
    {
        HandleRemoteAck(ackNumber);
    }

    internal void HandleRemoteAck(uint remoteAckNumber)
    {
        _messageProcessor?.AcknowledgeInFlight(remoteAckNumber);
    }

    internal List<TcpMessage> IngestReceivedMessage(TcpMessage message)
    {
        return _messageProcessor?.IngestReceivedMessage(message);
    }

    internal void SetFiber(PeerFiber fiber)
    {
        _fiber = fiber;
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

    internal void OnSessionAuthCompleted()
    {
        // 정책 적용
        var n = _session.Config.Network;
        var g = new PolicyGlobals(n.UseEncrypt, n.UseCompress, n.CompressionThreshold);
        var snapshot = ProtocolPolicyRegistry.CreateSnapshot(g);
        Interlocked.Exchange(ref _policySnapshot, snapshot);
    }

    internal void Online(ISession session)
    {
        Interlocked.Exchange(ref _stateCode, PeerStateConst.Online);
        _session = (Session)session;

        if (_messageProcessor != null)
        {        
            var pendingMessages = _messageProcessor.DequeuePendingMessages();
            foreach (var message in pendingMessages)
            {
                using (message)
                {
                    _messageProcessor.PrepareReliableSend(message);
                    if (!session.TrySend(ChannelKind.Reliable, message)) continue;
                    Interlocked.Exchange(ref _lastSentAck, message.AckNumber);
                }
            }
        }
        
        OnOnline();
    }

    internal void Offline(CloseReason reason)
    {
        Interlocked.Exchange(ref _stateCode, PeerStateConst.Offline);
        OnOffline(reason);
    }

    internal void LeaveServer(CloseReason reason)
    {
        Interlocked.Exchange(ref _stateCode, PeerStateConst.Closed);
        PeerIdGenerator.Free(PeerId);
        _messageProcessor?.Dispose();
        _diffieHellman?.Dispose();
        _session.Peer = null;
        OnLeaveServer(reason);
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
