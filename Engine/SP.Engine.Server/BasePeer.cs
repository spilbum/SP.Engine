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
    ISession Session { get; }
    bool Send(IProtocolData data);
    void Close(CloseReason reason);
}

public abstract class BasePeer : IPeer, IDisposable
{
    private readonly Lz4Compressor _compressor;
    private readonly ReliableMessageProcessor _messageProcessor = new();
    private DiffieHellman _diffieHellman;
    private bool _disposed;
    private AesGcmEncryptor _encryptor;
    private IProtocolPolicySnapshot _policySnapshot;
    private int _stateCode = PeerStateConst.NotAuthenticated;
    private Session _session;
    private uint _lastSentAck;
    private DateTime _lastAckTime;
    private readonly object _ackLock = new();

    protected BasePeer(PeerKind kind, ISession session)
    {
        PeerId = PeerIdGenerator.Generate();
        Kind = kind;
        Logger = session.Logger;
        _session = (Session)session;
        _compressor = new Lz4Compressor(session.Config.Network.MaxFrameBytes);
        _messageProcessor.SetSendTimeoutMs(session.Config.Network.SendTimeoutMs);
        _messageProcessor.SetMaxRetryCount(session.Config.Network.MaxRetryCount);
        _messageProcessor.SetMaxAckDelayMs(session.Config.Network.MaxAckDelayMs);
        _messageProcessor.SetAckStepThreshold(session.Config.Network.AckStepThreshold);
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
        _messageProcessor = other._messageProcessor;
        _policySnapshot = other._policySnapshot;
    }

    public double LatencyAvgMs { get; private set; }
    public double LatencyJitterMs { get; private set; }
    public float PacketLossRate { get; private set; }
    public IEncryptor Encryptor => _encryptor;
    public ICompressor Compressor => _compressor;
    public IPEndPoint LocalEndPoint => Session.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => Session.RemoteEndPoint;
    public bool IsConnected => _stateCode is PeerStateConst.Authenticated or PeerStateConst.Online;
    
    public PeerFiber Fiber { get; private set; }

    internal byte[] LocalPublicKey => _diffieHellman?.PublicKey;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public PeerState State => (PeerState)_stateCode;
    public uint PeerId { get; private set; }
    public PeerKind Kind { get; }
    public ILogger Logger { get; }
    public ISession Session => _session;

    TProtocol ICommandContext.Deserialize<TProtocol>(IMessage message)
    {
        return message.Deserialize<TProtocol>(_encryptor, _compressor);
    }

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
        
        var sequenceNumber = originalChannel == ChannelKind.Reliable
            ? _messageProcessor.GetNextReliableSeq()
            : 0;

        var ackNumber = originalChannel == ChannelKind.Reliable
            ? _messageProcessor.LastSequenceNumber
            : 0;

        var targetChannel = originalChannel == ChannelKind.Unreliable && !_session.IsUdpAvailable
            ? ChannelKind.Reliable
            : originalChannel;
        
        switch (targetChannel)
        {
            case ChannelKind.Reliable:
            {
                var tcp = new TcpMessage();
                tcp.SetSequenceNumber(sequenceNumber);
                tcp.SetAckNumber(ackNumber);
                tcp.Serialize(data, policy, encryptor, compressor);

                if (tcp.SequenceNumber == 0)
                {
                    // 즉시 전송 (Internal/Fallback)
                    return _session.TrySend(targetChannel, tcp);
                }

                if (!IsConnected)
                {
                    _messageProcessor.EnqueuePendingMessage(tcp);
                    return true;
                }

                _messageProcessor.RegisterMessageState(tcp);
                
                if (!_session.TrySend(targetChannel, tcp)) 
                    return false;

                Interlocked.Exchange(ref _lastSentAck, tcp.AckNumber);
                return true;
            }
            case ChannelKind.Unreliable:
            {
                var udp = new UdpMessage();
                udp.SetSessionId(_session.SessionId);
                udp.Serialize(data, policy, encryptor, compressor);
                return _session.TrySend(targetChannel, udp);
            }
            default:
                throw new Exception($"Unknown channel: {targetChannel}");
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
            _messageProcessor.Clear();
        }

        _disposed = true;
    }

    public virtual void Tick()
    {
        if (!IsConnected) return;
        
        ProcessRetries();
        MaintainAckStatus();
    }

    private void ProcessRetries()
    {
        var (retries, failed) = _messageProcessor.ExtractRetryMessages();
        if (failed.Count > 0)
        {
            Logger.Warn("Connection terminated due to message delivery failure. sessionId: {0}, peerId: {1}, first seq: {2}, count: {3}",
                _session.SessionId, PeerId, failed[0].SequenceNumber, failed.Count);
            
            Close(CloseReason.LimitExceededRetry);
            return;
        }
        
        foreach (var message in retries)
        {
            _session.TrySend(ChannelKind.Reliable, message);
        }
    }

    private void MaintainAckStatus()
    {
        var ackNumber = _messageProcessor.LastSequenceNumber;
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
            
            //Logger.Debug("[Ack] MaintainAckStatus sent: {0}", ackNumber);
        }
    }

    internal void OnPing(ushort rttMs, ushort avgRttMs, ushort jitterMs, byte packetLossRate)
    {
        _messageProcessor.AddRtoSample(rttMs);
        LatencyAvgMs = avgRttMs;
        LatencyJitterMs = jitterMs;
        PacketLossRate = packetLossRate;
    }

    internal void OnMessageAck(uint ackNumber)
    {
        HandleRemoteAck(ackNumber);
    }

    internal void HandleRemoteAck(uint ackNumber)
    {
        if (ackNumber <= 0) return;
        _messageProcessor.RemoveMessageStates(ackNumber);
        //Logger.Debug("[Ack] Remote acknowledged up to: {0}", ackNumber);
    }

    internal List<TcpMessage> ProcessMessageInOrder(TcpMessage message)
    {
        return _messageProcessor.ProcessMessageInOrder(message);
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

        foreach (var message in _messageProcessor.DequeuePendingMessages())
        {
            // 재전송 트래커에 등록
            _messageProcessor.RegisterMessageState(message);
                
            // 전송
            session.TrySend(ChannelKind.Reliable, message);
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
        PeerId = 0;
        _messageProcessor.Clear();
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
