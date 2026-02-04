using System.Collections.Concurrent;
using System.Net;
using Common;
using RankServer.Season;
using RankServer.ServerPeer;
using SP.Engine.Server;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Connector;
using DatabaseHandler;
using SP.Core;
using SP.Shared.Resource;
using SP.Shared.Rank.Season;
using SP.Shared.Resource.Web;
using ErrorCode = Common.ErrorCode;

namespace RankServer;

public static class RankSeasonExtensions
{
    public static bool TryGetInfo(this IRankSeason season, long uid, out PlayerRankInfo? info)
    {
        switch (season)
        {
            case DailyRankSeason daily:
                info = daily.GetInfo(uid);
                return info != null;
            default:
                info = null;
                return false;
        }
    }

    public static bool TryGetTopInfos(this IRankSeason season, int count, out List<PlayerRankInfo> infos)
    {
        switch (season)
        {
            case DailyRankSeason daily:
                infos = daily.GetTopInfos(count);
                return infos.Count > 0;
            default:
                infos = [];
                return false;
        }
    }

    public static bool TryGetRangeInfos(this IRankSeason season, int startRank, int count,
        out List<PlayerRankInfo> infos)
    {
        switch (season)
        {
            case DailyRankSeason daily:
                infos = daily.GetRangeInfos(startRank, count);
                return infos.Count > 0;
            default:
                infos = [];
                return false;
        }
    }

    public static bool UpdateRecord(this IRankSeason season, long uid, int deltaScore, int? absoluteScore, string? name,
        string? countryCode)
    {
        return season switch
        {
            DailyRankSeason daily => daily.UpdateRecord(uid, deltaScore, name, countryCode),
            _ => false
        };
    }
}

public class RankServer : Engine
{
    private static readonly HttpClient Http = new(
        new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            PooledConnectionIdleTimeout = TimeSpan.FromMinutes(2),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        })
    {
        Timeout = TimeSpan.FromSeconds(15)
    };
    
    private readonly ConcurrentDictionary<SeasonKind, IRankSeason> _seasons = new();
    private readonly MySqlDbConnector _dbConnector = new();
    private readonly List<string> _acceptServers = [];
    private readonly CancellationToken _shutdown;
    private HttpRpc? _resourceRpc;
    
    public ServerGroupType GroupType { get; private set; }
    
    public RankServer(CancellationToken shutdown)
    {
        Instance = this;
        _shutdown = shutdown;
    }

    public static RankServer Instance { get; private set; } = null!;
    public RankRepository Repository { get; private set; } = null!;

    public bool Initialize(BuildConfig buildConfig)
    {
        var config = EngineConfigBuilder.Create()
            .WithNetwork(n => n with
            {
            })
            .WithSession(s => s with
            {
            })
            .WithPerf(r => r with
            {
                MonitorEnabled = true,
                SamplePeriod = TimeSpan.FromSeconds(1),
                LoggerEnabled = true,
                LoggingPeriod = TimeSpan.FromSeconds(30)
            })
            .AddListener(new ListenerConfig { Ip = "Any", Port = buildConfig.Server.Port })
            .Build();

        if (!base.Initialize(buildConfig.Server.Name, config))
            return false;

        if (!Enum.TryParse(buildConfig.Server.GroupType, out ServerGroupType groupType))
            return false;
        
        GroupType = groupType;

        foreach (var database in buildConfig.Database)
        {
            if (!Enum.TryParse(database.Kind, true, out DbKind kind) ||
                string.IsNullOrEmpty(database.ConnectionString))
                return false;
        
            _dbConnector.Register(kind, database.ConnectionString);
        }

        foreach (var allowed in buildConfig.Server.AcceptServers)
        {
            _acceptServers.Add(allowed);
        }

        Repository = new RankRepository(_dbConnector);
        CreateDailyRankSeason();
        
        SetupResource(buildConfig.Resource);
        return true;
    }

    private void SetupResource(ResourceConfig config)
    {
        _resourceRpc = new HttpRpc(Http, config.BaseUrl);
        
        var ts = TimeSpan.FromSeconds(config.SyncPeriodSec);
        Scheduler.ScheduleAsync(async ct =>
        {
            var list = new List<ServerSyncInfo>();
            var sessions = GetAllSessions();
            foreach (var session in sessions)
            {
                if (session is Session { Peer: GameServerPeer game })
                {
                    list.Add(new ServerSyncInfo(
                        $"{game.ProcessId}",
                        "Game",
                        "kr",
                        game.Host,
                        game.Port,
                        "1.0.1",
                        ServerStatus.Online,
                        null,
                        game.UpdatedUtc
                    ));
                }
            }

            var req = new SyncServerListReq
            {
                List = list,
                ServerGroupType = GroupType,
                UpdatedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            try
            {
                await _resourceRpc.CallAsync<SyncServerListReq, SyncServerListRes>(
                    ResourceMsgId.SyncServerListReq, req, ct);
            }
            catch (RpcException ex)
            {
                Logger.Error($"RpcException: {ex.Message}, error={ex.Error}");
            }
          
        }, ts, ts, _shutdown);
    }

    private void CreateDailyRankSeason()
    {
        var options = new RankSeasonOptions
        {
            RankedCapacity = 50_000,
            ChunkSize = 1_000,
            OutOfRankRatio = 0.3f,
            RankOrder = RankOrder.HigherIsBetter,
            MaxUpdatesPerTick = 100
        };

        var daily = new DailyRankSeason(Logger);
        daily.Initialize(options);
        _seasons[daily.Kind] = daily;
    }

    public override bool Start()
    {
        if (!base.Start())
            return false;

        foreach (var season in _seasons.Values)
            season.Start();

        return true;
    }

    protected override IPeer CreatePeer(ISession session)
    {
        return new BaseServerPeer(session);
    }

    protected override IConnector CreateConnector(string name)
    {
        throw new NotImplementedException();
    }

    public ErrorCode RegisterPeer(S2SProtocolData.RegisterReq req, BaseServerPeer peer)
    {
        if (!_acceptServers.Contains(req.ServerKind))
            return ErrorCode.InternalError;
        
        BasePeer newPeer;
        switch (req.ServerKind)
        {
            case "Game":
                var gs = new GameServerPeer(peer, req.ProcessId)
                {
                    BuildVersion = req.BuildVersion,
                    Host = req.IpAddress,
                    Port = req.OpenPort
                };
                newPeer = gs;
                break;
            
            default:
                Logger.Warn("Unknown server: {0}", req.ServerKind);
                return ErrorCode.InternalError;
        }
        
        if (!ChangeServerPeer(newPeer))
        {
            Logger.Error("Failed to change peer. kind={0}", req.ServerKind);
            return ErrorCode.InternalError;
        }

        Logger.Info("Server {0} registered.", req.ServerKind);
        return ErrorCode.Ok;
    }

    public bool TryGetSeason(SeasonKind kind, out IRankSeason? season)
        => _seasons.TryGetValue(kind, out season);
}
