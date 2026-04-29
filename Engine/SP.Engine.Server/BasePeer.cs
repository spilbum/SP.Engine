using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
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
    ISession Session { get; }
    bool Send(IProtocolData data);
    void Close(CloseReason reason);
}

public abstract class BasePeer : IPeer, IDisposable
{
    private readonly Lz4Compressor _compressor;
    private DiffieHellman _diffieHellman;
    private bool _disposed;
    private AesGcmEncryptor _encryptor;
    private IProtocolPolicySnapshot _policySnapshot;
    private int _stateCode = PeerStateConst.NotAuthenticated;
    private Session _session;
    private uint _lastSentAck;
    private DateTime _lastAckTime;
    private readonly object _ackLock = new();
    private int _pendingJobCount;
    
    protected BasePeer(PeerKind kind, ISession session)
    {
        PeerId = PeerIdGenerator.Generate();
        Kind = kind;
        Logger = session.Logger;
        _session = (Session)session;
        _session.Peer = this;
        
        var config = session.Config;
        _compressor = new Lz4Compressor(config.Network.MaxFrameBytes);
        MessageProcessor.SetSendTimeoutMs(config.Network.SendTimeoutMs);
        MessageProcessor.SetMaxRetransmissionCount(config.Network.MaxRetransmissionCount);
        MessageProcessor.SetMaxAckDelayMs(config.Network.MaxAckDelayMs);
        MessageProcessor.SetAckStepThreshold(config.Network.AckStepThreshold);
        MessageProcessor.SetMaxOutOfOrder(config.Network.MaxOutOfOderCount);
        
        _policySnapshot = ProtocolPolicyRegistry.CreateSnapshot(PolicyDefaults.FallbackGlobals);
    }

    protected BasePeer(BasePeer other)
    {
        PeerId = other.PeerId;
        Kind = other.Kind;
        Logger = other.Logger;
        Fiber = other.Fiber;
        _session = (Session)other.Session;
        _stateCode = other._stateCode;
        _diffieHellman = other._diffieHellman;
        _encryptor = other._encryptor;
        _compressor = other._compressor;
        MessageProcessor = other.MessageProcessor;
        _policySnapshot = other._policySnapshot;
    }

    public double LatencyAvgMs { get; private set; }
    public double LatencyJitterMs { get; private set; }
    public IEncryptor Encryptor => _encryptor;
    public ICompressor Compressor => _compressor;
    public IPEndPoint LocalEndPoint => Session.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => Session.RemoteEndPoint;
    public bool IsConnected => _stateCode is PeerStateConst.Authenticated or PeerStateConst.Online;
    public ReliableMessageProcessor MessageProcessor { get; } = new();
    public PeerFiber Fiber { get; private set; }
    internal byte[] LocalPublicKey => _diffieHellman?.PublicKey;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }
    
    public void EnqueueJob<T1, T2>(Action<T1, T2> action, T1 s1, T2 s2)
    {
        var count = Interlocked.Increment(ref _pendingJobCount);
        if (count >= _session.Config.Session.PeerJobBackPressureThreshold)
        {
            if (!_session.IsPaused)
            {
                Logger.Warn("BackPressure Enabled. PeerId: {0}, SessionId: {1}, PendingJobs: {2}",
                    PeerId, _session.SessionId, count);
                _session.PauseReceive();   
            }
        }

        Fiber.EnqueueJob(this, action, s1, s2);
    }

    internal void OnJobFinished()
    {
        var count = Interlocked.Decrement(ref _pendingJobCount);
        if (count > _session.Config.Session.PeerJobResumeThreshold) return;
        if (!_session.IsPaused) return;
            
        Logger.Info("[BackPressure] Disabled. PeerId: {0}, SessionId: {1}, PendingJobs: {2}",
            PeerId, _session.SessionId, count);
        _session.ResumeReceive();
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
        _session.Close(reason);
    }

    public bool Send(IProtocolData data)
    {
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
                    tcp.Serialize(data, policy, encryptor, compressor);

                    if (originalChannel == ChannelKind.Unreliable)
                        return _session.TrySend(channel, tcp);   

                    if (IsConnected)
                    {
                        MessageProcessor.PrepareReliableSend(tcp);
                        if (!_session.TrySend(channel, tcp)) return false;
                        Interlocked.Exchange(ref _lastSentAck, tcp.AckNumber);
                    }
                    else
                    {
                        MessageProcessor.EnqueuePendingMessage(tcp);
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
            PeerId = 0;
            Fiber = null;
            _lastSentAck = 0;
            _diffieHellman?.Dispose();
            _diffieHellman = null;
            _encryptor = null;
            MessageProcessor.Dispose();
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
        var (retries, failed) = MessageProcessor.ProcessRetransmissions();
        if (failed.Count > 0)
        {
            // Logger.Debug("Connection terminated due to message delivery failure. sessionId: {0}, peerId: {1}, first seq: {2}, count: {3}",
            //     _session.SessionId, PeerId, failed[0].SequenceNumber, failed.Count);
            
            foreach (var m in failed)
                m.Dispose();
            MessageProcessor.RestartInFlightMessages();
            
            Close(CloseReason.LimitExceededRetransmission);
            return;
        }

        foreach (var message in retries.Where(message => _session.TrySend(ChannelKind.Reliable, message)))
        {
            Interlocked.Exchange(ref _lastSentAck, message.AckNumber);
        }
    }

    private void MaintainAckStatus()
    {
        var ackNumber = MessageProcessor.LastReceivedSeq;
        if (ackNumber <= _lastSentAck) return;
        
        var now = DateTime.UtcNow;
        var elapsedMs = (now - _lastAckTime).TotalMilliseconds;
        var pendingCount = ackNumber - _lastSentAck;

        if (elapsedMs < MessageProcessor.MaxAckDelayMs && pendingCount < MessageProcessor.AckStepThreshold)
            return;

        lock (_ackLock)
        {
            if (ackNumber <= _lastSentAck) return;
            if (elapsedMs < MessageProcessor.MaxAckDelayMs && pendingCount < MessageProcessor.AckStepThreshold)
                return;

            _lastAckTime = DateTime.UtcNow;
            _lastSentAck = ackNumber;
            _session.SendMessageAck(ackNumber);
        }
    }

    internal void OnPing(ushort rttMs, ushort avgRttMs, ushort jitterMs)
    {
        MessageProcessor.AddRtoSample(rttMs);
        LatencyAvgMs = avgRttMs;
        LatencyJitterMs = jitterMs;
    }

    internal void OnMessageAck(uint ackNumber)
    {
        HandleRemoteAck(ackNumber);
    }

    internal void HandleRemoteAck(uint remoteAckNumber)
    {
        MessageProcessor.AcknowledgeInFlight(remoteAckNumber);
    }

    internal void SetFiber(PeerFiber fiber)
    {
        Fiber = fiber;
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

        var pendingMessages = MessageProcessor.DequeuePendingMessages();
        foreach (var message in pendingMessages)
        {
            using (message)
            {
                MessageProcessor.PrepareReliableSend(message);
                if (!session.TrySend(ChannelKind.Reliable, message)) continue;
                Interlocked.Exchange(ref _lastSentAck, message.AckNumber);
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
        MessageProcessor.Dispose();
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
        return $"sessionId={Session.SessionId}, peerId={PeerId}, peerType={Kind}, remoteEndPoint={RemoteEndPoint}";
    }

    private static class PeerIdGenerator
    {
        private static readonly ConcurrentQueue<uint> FreePeerIdPool = new();
        private static uint _latestPeerId;

        public static uint Generate()
        {
            if (_latestPeerId >= uint.MaxValue) throw new InvalidOperationException("ID overflow detected");
            return FreePeerIdPool.TryDequeue(out var peerId)
                ? peerId
                : Interlocked.Increment(ref _latestPeerId);
        }

        public static void Free(uint peerId)
        {
            if (peerId > 0)
                FreePeerIdPool.Enqueue(peerId);
        }
    }
}
