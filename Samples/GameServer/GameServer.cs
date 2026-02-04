using System.Collections.Concurrent;
using Common;
using GameServer.Connector;
using GameServer.Matchmaking;
using GameServer.Room;
using GameServer.UserPeer;
using SP.Core;
using SP.Engine.Runtime;
using SP.Engine.Server;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Connector;
using DatabaseHandler;

namespace GameServer;

public class GameServer : Engine
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
    public NetworkEnv Env => _networkInfo?.Env ?? NetworkEnv.Unknown;
    public string Region => _networkInfo?.Region ?? string.Empty;
    public string PublicIpAddress => _networkInfo?.PublicIpAddress ?? string.Empty;
    public string PrivateIpAddress => _networkInfo?.PrivateIpAddress ?? string.Empty;
    public string PublicDnsName => _networkInfo?.DnsName ?? string.Empty;
    public int OpenPort { get; private set; }
    public string BuildVersion { get; private set; }

    public GameRoomManager RoomManager { get; private set; } = null!;
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

    public bool Initialize(AppConfig appConfig)
    {
        var builder = EngineConfigBuilder.Create()
            .WithNetwork(n => n with
            {
            })
            .WithSession(s => s with
            {
            })
            .WithPerf(r => r with
            {
                LoggerEnabled = false,
                LoggingPeriod = TimeSpan.FromSeconds(15)
            })
            .AddListener(new ListenerConfig { Ip = "Any", Port = appConfig.Server.Port });

        foreach (var connector in appConfig.Connector)
        {
            builder.AddConnector(new SP.Engine.Server.Configuration.ConnectorConfig
                { Name = connector.Name, Host = connector.Host, Port = connector.Port });   
        }

        var config = builder.Build();
        if (!base.Initialize(appConfig.Server.Name, config))
            return false;

        OpenPort = appConfig.Server.Port;

        foreach (var database in appConfig.Database)
        {
            if (!Enum.TryParse(database.Kind, true, out DbKind kind) ||
                string.IsNullOrEmpty(database.ConnectionString))
                return false;

            _connector.Register(kind, database.ConnectionString);
        }

        Repository = new GameRepository(_connector);
        RoomManager = new GameRoomManager();
        Matchmaker = new Matchmaker(
            RoomManager,
            TimeSpan.FromMilliseconds(200),
            TimeSpan.FromSeconds(5));
        
        if (!HostNetworkInfoProvider.TryGet(out _networkInfo, TimeSpan.FromSeconds(5)))
            Logger.Warn("No network info available.");
        
        return true;
    }

    public override bool Start()
    {
        if (!base.Start())
            return false;
        
        Logger.Info("Env={0}, Region={1}, Public={2}, Private={3}, DnsName={4}", Env, Region, PublicIpAddress,
            PrivateIpAddress, PublicDnsName);
        return true;
    }

    public override void Stop()
    {
        base.Stop();
        RoomManager.Stop();
    }

    protected override IPeer CreatePeer(ISession session)
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

        peer = GetPeer<GamePeer>(peerId);
        return peer != null;
    }
    
    public void Bind(GamePeer peer)
    {
        if (_byUid.TryAdd(peer.Uid, peer.PeerId))
            Logger.Debug("Bind peer: uid={0}, peerId={1}", peer.Uid, peer.PeerId);
    }

    private void Unbind(GamePeer peer)
    {
        if (_byUid.TryRemove(peer.Uid, out var peerId))
            Logger.Debug("Unbind peer: uid={0}, peerId={1}", peer.Uid, peerId);
    }

    protected override void OnPeerLeaved(BasePeer peer, CloseReason reason)
    {
        if (peer is not GamePeer gp)
            return;

        Unbind(gp);
        Logger.Debug("Peer leaved. uid={0}, reason={1}", gp.Uid, reason);
    }
}
