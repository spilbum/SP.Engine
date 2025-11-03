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
    private readonly ReliableMessageProcessor _reliableMessageProcessor;
    private DiffieHellman _diffieHellman;
    private bool _disposed;
    private AesCbcEncryptor _encryptor;
    private IPolicyView _networkPolicy = new NetworkPolicyView(in PolicyDefaults.Globals);

    private int _stateCode = PeerStateConst.NotAuthenticated;

    protected BasePeer(PeerKind kind, ISession session)
    {
        PeerId = PeerIdGenerator.Generate();
        Kind = kind;
        Logger = session.Logger;
        Session = session;
        _compressor = new Lz4Compressor(session.Config.Network.MaxFrameBytes);
        _reliableMessageProcessor = new ReliableMessageProcessor(Logger);
        _reliableMessageProcessor.SetSendTimeoutMs(session.Config.Network.SendTimeoutMs);
        _reliableMessageProcessor.SetMaxRetryCount(session.Config.Network.MaxRetryCount);
    }

    protected BasePeer(BasePeer other)
    {
        PeerId = other.PeerId;
        Kind = other.Kind;
        Logger = other.Logger;
        Session = other.Session;
        _stateCode = other._stateCode;
        _diffieHellman = other._diffieHellman;
        _encryptor = other._encryptor;
        _compressor = other._compressor;
        _networkPolicy = other._networkPolicy;
        _reliableMessageProcessor = other._reliableMessageProcessor;
    }

    public double LatencyAvgMs { get; private set; }
    public double LatencyJitterMs { get; private set; }
    public float PacketLossRate { get; private set; }
    public IEncryptor Encryptor => _encryptor;
    public ICompressor Compressor => _compressor;
    public IPEndPoint LocalEndPoint => Session.LocalEndPoint;
    public IPEndPoint RemoteEndPoint => Session.RemoteEndPoint;
    public bool IsConnected => _stateCode is PeerStateConst.Authenticated or PeerStateConst.Online;

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
    public ISession Session { get; private set; }

    TProtocol ICommandContext.Deserialize<TProtocol>(IMessage message)
    {
        return message.Deserialize<TProtocol>(_encryptor, _compressor);
    }

    public void Close(CloseReason reason)
    {
        Session.Close(reason);
    }

    public bool Send(IProtocolData data)
    {
        var channel = data.Channel;
        var policy = GetNetworkPolicy(data.GetType());
        var encryptor = policy.UseEncrypt ? Encryptor : null;
        var compressor = policy.UseCompress ? Compressor : null;
        switch (channel)
        {
            case ChannelKind.Reliable:
            {
                var msg = new TcpMessage();
                var seq = _reliableMessageProcessor.GetNextReliableSeq();
                msg.SetSequenceNumber(seq);
                msg.Serialize(data, policy, encryptor, compressor);

                if (!IsConnected)
                {
                    _reliableMessageProcessor.EnqueuePendingMessage(msg);
                    return true;
                }

                _reliableMessageProcessor.RegisterMessageState(msg);
                return Session.TrySend(channel, msg);
            }
            case ChannelKind.Unreliable:
            {
                var msg = new UdpMessage();
                msg.SetPeerId(PeerId);
                msg.Serialize(data, policy, encryptor, compressor);
                return Session.TrySend(channel, msg);
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
            _diffieHellman?.Dispose();
            _diffieHellman = null;
            _encryptor?.Dispose();
            _encryptor = null;
            _reliableMessageProcessor.Reset();
        }

        _disposed = true;
    }

    public virtual void Tick()
    {
        if (!IsConnected)
            return;

        if (_reliableMessageProcessor.TryGetRetryMessages(out var retries))
            foreach (var message in retries)
                Session.TrySend(ChannelKind.Reliable, message);
        else
            Close(CloseReason.LimitExceededRetry);
    }

    internal void OnPing(double rttMs, double avgRttMs, double jitterMs, float packetLossRate)
    {
        _reliableMessageProcessor.AddRtoSample(rttMs);
        LatencyAvgMs = avgRttMs;
        LatencyJitterMs = jitterMs;
        PacketLossRate = packetLossRate;
    }

    internal void OnMessageAck(long sequenceNumber)
    {
        _reliableMessageProcessor.RemoveMessageState(sequenceNumber);
    }

    internal List<IMessage> ProcessMessageInOrder(IMessage message)
    {
        return _reliableMessageProcessor.ProcessMessageInOrder(message);
    }

    public IPolicy GetNetworkPolicy(Type protocolType)
    {
        return _networkPolicy.Resolve(protocolType);
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

    internal void OnSessionAuthenticated()
    {
        // 정책 생성
        var n = Session.Config.Network;
        var g = new PolicyGlobals(n.UseEncrypt, n.UseCompress, n.CompressionThreshold);
        var newView = new NetworkPolicyView(g);
        Interlocked.Exchange(ref _networkPolicy, newView);
    }

    internal void Online(ISession session)
    {
        Interlocked.Exchange(ref _stateCode, PeerStateConst.Online);
        Session = session;

        foreach (var message in _reliableMessageProcessor.DequeuePendingMessages())
            session.TrySend(ChannelKind.Reliable, message);

        OnOnline();
    }

    internal void Offline(CloseReason reason)
    {
        Interlocked.Exchange(ref _stateCode, PeerStateConst.Offline);
        _networkPolicy = new NetworkPolicyView(PolicyDefaults.Globals);
        _reliableMessageProcessor.ResetAllMessageStates();
        OnOffline(reason);
    }

    internal void LeaveServer(CloseReason reason)
    {
        Interlocked.Exchange(ref _stateCode, PeerStateConst.Closed);
        PeerIdGenerator.Free(PeerId);
        PeerId = 0;
        _reliableMessageProcessor.Reset();
        OnLeaveServer(reason);
    }

    internal bool TryKeyExchange(DhKeySize keySize, byte[] peerPublicKey)
    {
        if (peerPublicKey == null || peerPublicKey.Length == 0)
        {
            Logger?.Warn("Key exchange validation failed: peer public key is empty.");
            return false;
        }

        byte[] shared = null;

        try
        {
            _diffieHellman = new DiffieHellman(keySize);
            shared = _diffieHellman.DeriveSharedKey(peerPublicKey);
            _encryptor = new AesCbcEncryptor(shared);
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
            if (shared != null)
                CryptographicOperations.ZeroMemory(shared);
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
        return $"sessionId={Session.Id}, peerId={PeerId}, peerType={Kind}, remoteEndPoint={RemoteEndPoint}";
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
