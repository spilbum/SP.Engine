using System.Collections.Concurrent;
using System.Net;
using System.Reflection;
using Common;
using RankServer.Season;
using RankServer.ServerPeer;
using SP.Engine.Server;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Connector;
using DatabaseHandler;
using SP.Core;
using SP.Core.Fiber;
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

public class RankServer : EngineBase
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
    private HttpRpc? _resourceRpc;
    private ThreadFiber? _serverFiber;
    private CancellationTokenSource _shutdownCts = new();

    public ServerGroupType ServerGroupType { get; private set; }
    
    public RankServer()
    {
        Instance = this;
    }

    public static RankServer Instance { get; private set; } = null!;
    public RankRepository Repository { get; private set; } = null!;

    public bool Setup(AppConfig config)
    {
        if (!Enum.TryParse(config.Server.Group, out ServerGroupType group)) return false;
        ServerGroupType = group;
        
        _serverFiber = new ThreadFiber("ServerFiber",
            capacity: 8 * 1024,
            maxBatchSize: 1024,
            onError: ex => Logger.Error(ex));
        
        foreach (var allowed in config.Server.AcceptServers)
        {
            _acceptServers.Add(allowed);
        }
        
        if (!SetupDatabases(config.Database))
            return false;
        
        if (!SetupResource(config.Resource))
            return false;
        
        SetupDailyRankSeason();
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

            _dbConnector.Register(kind, config.ConnectionString);
        }
        
        Repository = new RankRepository(_dbConnector);
        return true;
    }

    private bool SetupResource(ResourceConfig config)
    {
        if (string.IsNullOrEmpty(config.BaseUrl))
            return false;
        
        _resourceRpc = new HttpRpc(Http, config.BaseUrl);
        
        GlobalScheduler.Schedule(
            _serverFiber, SyncServerListSchedule, 
            TimeSpan.Zero, TimeSpan.FromSeconds(config.SyncPeriodSec));
        return true;
    }

    private void SyncServerListSchedule()
    {
        var list = GetAllSessions()
            .Where(s => s.Peer is GameServerPeer)
            .Select(s => {
                var game = (GameServerPeer)s.Peer;
                return new ServerSyncInfo(
                    game.ProcessId.ToString(),
                    "Game",
                    "kr",
                    game.Host,
                    game.Port,
                    "1.0.1",
                    ServerStatus.Online,
                    null,
                    game.UpdatedUtc
                );
            })
            .ToList();

        if (list.Count == 0) return;
        
        _serverFiber.RunAsync(ct => SyncServerListAsync(list, ct), null, ex =>
        {
            Logger.Error("Failed to sync server list: {0}", ex.Message);
        }, _shutdownCts.Token);
    }
    
    private async Task SyncServerListAsync(List<ServerSyncInfo> list, CancellationToken ct)
    {
        var req = new SyncServerListReq
        {
            List = list,
            ServerGroupType = ServerGroupType,
            UpdatedUtcMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
        };

        if (_resourceRpc != null)
        {
            await _resourceRpc.CallAsync<SyncServerListReq, SyncServerListRes>(
                ResourceMsgId.SyncServerListReq, req, ct);   
        }
    }

    private void SetupDailyRankSeason()
    {
        var options = new RankSeasonOptions
        {
            RankedCapacity = 50_000,
            ChunkSize = 1_000,
            OutOfRankRatio = 0.3f,
            RankOrder = RankOrder.HigherIsBetter,
            MaxUpdatesPerTick = 100
        };

        var daily = new DailyRankSeason();
        daily.Initialize(options);
        _seasons[daily.Kind] = daily;
    }

    protected override void OnStarted()
    {
        foreach (var season in _seasons.Values) season.Start();
    }

    protected override void OnStopped()
    {
        _shutdownCts.Cancel();
    }

    protected override IPeer CreatePeer(Session session)
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
        
        PeerBase newPeer;
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
        
        if (!TransitionTo(newPeer))
        {
            Logger.Error("Failed to change peer. kind={0}", req.ServerKind);
            return ErrorCode.InternalError;
        }

        Logger.Info("Server {0} registered: {1}:{2}", req.ServerKind, req.IpAddress, req.OpenPort);
        return ErrorCode.Ok;
    }

    public bool TryGetSeason(SeasonKind kind, out IRankSeason? season)
        => _seasons.TryGetValue(kind, out season);
}
