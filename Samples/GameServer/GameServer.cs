using System.Collections.Concurrent;
using System.Reflection;
using Common;
using GameServer.Connector;
using GameServer.Matchmaking;
using GameServer.Room;
using GameServer.UserPeer;
using SP.Core;
using SP.Engine.Server;
using SP.Engine.Server.Connector;
using DatabaseHandler;
using Version;

namespace GameServer;

public class GameServer : EngineBase
{
    private readonly ConcurrentDictionary<long, uint> _byUid = new();
    private HostNetworkInfo? _networkInfo;

    public GameServer()
    {
        Instance = this;
        BuildVersion = Version.Server.BuildVersion;
    }

    public static GameServer Instance { get; private set; } = null!;

    private readonly MySqlDbConnector _connector = new();

    public string ServerGroupType { get; set; } = "";
    public NetworkEnv Env => _networkInfo?.Env ?? NetworkEnv.Unknown;
    public string Region => _networkInfo?.Region ?? string.Empty;
    public string PublicIpAddress => _networkInfo?.PublicIpAddress ?? string.Empty;
    public string PrivateIpAddress => _networkInfo?.PrivateIpAddress ?? string.Empty;
    public string PublicDnsName => _networkInfo?.DnsName ?? string.Empty;
    public int OpenPort => GetOpenPort(SocketMode.Tcp);
    public string BuildVersion { get; private set; }

    public GameRoomManager RoomManager { get; private set; } = new();
    public Matchmaker Matchmaker { get; private set; } = null!;
    public GameRepository Repository { get; private set; } = null!;

    public string GetIpAddress()
    {
        return Env switch
        {
            NetworkEnv.Local => PrivateIpAddress,
            NetworkEnv.AwsEc2 => PublicIpAddress,
            _ => "127.0.0.1"
        };
    }

    protected override void OnStarted()
    {
        Logger.Info("Group={0}, Name={1}, Env={2}, Region={3}, Public={4}, Private={5}, DnsName={6}",
            ServerGroupType, Name, Env, Region, PublicIpAddress, PrivateIpAddress, PublicDnsName);
    }

    public bool Setup(AppConfig config)
    {
        ServerGroupType = config.Server.Group;
        
        if (!SetupDatabases(config.Database))
            return false;
        
        if (!SetupMatchmaker())
            return false;
        
        if (!HostNetworkInfoProvider.TryGet(out _networkInfo, TimeSpan.FromSeconds(5)))
            Logger.Warn("No network info available.");

        return true;
    }

    private bool SetupMatchmaker()
    {
        Matchmaker = new Matchmaker(
            RoomManager,
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromSeconds(5));
        return true;
    }

    private bool SetupDatabases(DatabaseConfig[] configs)
    {
        foreach (var config in configs)
        {
            if (!Enum.TryParse(config.Kind, true, out DbKind kind) ||
                string.IsNullOrEmpty(config.ConnectionString))
            {
                return false;
            }

            _connector.Register(kind, config.ConnectionString);
        }
        
        Repository = new GameRepository(_connector);
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            RoomManager.Dispose();
        }
        
        base.Dispose(disposing);
    }

    protected override IPeer CreatePeer(Session session)
    {
        return new GamePeer(session);
    }

    protected override IConnector? CreateConnector(string name)
    {
        try
        {
            var connector = name switch
            {
                "Rank" => new RankConnector(),
                _ => throw new InvalidCastException($"Unknown connector name: {name}")
            };

            return connector;
        }
        catch (Exception e)
        {
            Logger.Error(e);
            return null;
        }
    }

    public RankConnector? GetRankConnector()
    {
        return GetConnectors("Rank").SingleOrDefault() as RankConnector;
    }

    public bool TryGetPeer(long uid, out GamePeer? peer)
    {
        peer = null;

        if (!_byUid.TryGetValue(uid, out var peerId))
            return false;

        peer = GetActivePeer<GamePeer>(peerId);
        return peer != null;
    }
    
    public void Bind(GamePeer peer)
    {
        if (_byUid.TryAdd(peer.Uid, peer.PeerId))
        {
            Logger.Debug("Bind peer: uid={0}, peerId={1}", peer.Uid, peer.PeerId);
        }
    }

    public void Unbind(GamePeer peer)
    {
        if (_byUid.TryRemove(peer.Uid, out var peerId))
        {
            Logger.Debug("Unbind peer: uid={0}, peerId={1}", peer.Uid, peerId);
        }
    }
}
