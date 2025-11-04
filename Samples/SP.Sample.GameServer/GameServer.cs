using System.Collections.Concurrent;
using SP.Core;
using SP.Engine.Runtime;
using SP.Engine.Server;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Connector;
using SP.Sample.Common;
using SP.Sample.DatabaseHandler;
using SP.Sample.GameServer.Connector;
using SP.Sample.GameServer.Matchmaking;
using SP.Sample.GameServer.Room;
using SP.Sample.GameServer.UserPeer;

namespace SP.Sample.GameServer;

public class GameServer : Engine.Server.Engine
{
    private readonly ConcurrentDictionary<long, uint> _byUid = new();

    private HostNetworkInfo? _networkInfo;
    private int _openPort;

    public GameServer()
    {
        Instance = this;
        ServerId = 1;
    }

    public static GameServer Instance { get; private set; } = null!;

    private readonly MySqlDbConnector _connector = new();
    public byte ServerId { get; private set; }
    public NetworkEnv Env => _networkInfo?.Env ?? NetworkEnv.Unknown;
    public string Region => _networkInfo?.Region ?? string.Empty;
    public string PublicIpAddress => _networkInfo?.PublicIpAddress ?? string.Empty;
    public string PrivateIpAddress => _networkInfo?.PrivateIpAddress ?? string.Empty;
    public string PublicDnsName => _networkInfo?.DnsName ?? string.Empty;
    public GameRoomManager RoomManager { get; private set; } = null!;
    public Matchmaker Matchmaker { get; private set; } = null!;
    public GameRepository Repository { get; private set; } = null!;

    public string? GetIpAddress()
    {
        return Env switch
        {
            NetworkEnv.Local => PrivateIpAddress,
            NetworkEnv.AwsEc2 => PublicIpAddress,
            _ => null
        };
    }

    public int GetPort()
    {
        return _openPort;
    }

    public bool Initialize(AppConfig appConfig)
    {
        var builder = new EngineConfigBuilder()
            .WithNetwork(n => n with
            {
            })
            .WithSession(s => s with
            {
            })
            .WithRuntime(r => r with
            {
                PrefLoggerEnabled = false,
                PerfLoggingPeriod = TimeSpan.FromSeconds(15)
            })
            .AddListener(new ListenerConfig { Ip = "Any", Port = appConfig.Server.Port });

        foreach (var connector in appConfig.Connector)
        {
            builder.AddConnector(new Engine.Server.Configuration.ConnectorConfig
                { Name = connector.Name, Host = connector.Host, Port = connector.Port });   
        }

        var config = builder.Build();
        if (!base.Initialize(appConfig.Server.Name, config))
            return false;

        _openPort = appConfig.Server.Port;

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
        return true;
    }

    public override bool Start()
    {
        if (!base.Start())
            return false;

        if (!HostNetworkInfoProvider.TryGet(out _networkInfo))
            Logger.Warn("No network info available.");

        Logger.Info("Env={0}, Region={1}, Public={2}, Private={3}, DnsName={4}", Env, Region, PublicIpAddress,
            PrivateIpAddress, PublicDnsName);
        return true;
    }

    public override void Stop()
    {
        base.Stop();
        RoomManager.Stop();
    }

    protected override bool TryCreatePeer(ISession session, out IPeer peer)
    {
        peer = new GamePeer(session);
        return true;
    }

    protected override bool TryCreateConnector(string name, out IConnector? connector)
    {
        connector = null;

        try
        {
            connector = name switch
            {
                "Rank" => new RankConnector(),
                _ => throw new InvalidCastException($"Unknown connector name: {name}")
            };

            return true;
        }
        catch (Exception e)
        {
            Logger.Error(e);
            return false;
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

        peer = FindPeerByPeerId<GamePeer>(peerId);
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
        Logger.Debug("Peer leaved. uid={0}, reason={2}", gp.Uid, reason);
    }
}
