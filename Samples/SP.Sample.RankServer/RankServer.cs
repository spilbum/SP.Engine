using System.Collections.Concurrent;
using SP.Engine.Server;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Connector;
using SP.Sample.Common;
using SP.Sample.DatabaseHandler;
using SP.Sample.RankServer.Season;
using SP.Sample.RankServer.ServerPeer;
using SP.Shared.Rank.Season;

namespace SP.Sample.RankServer;

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

public class RankServer : Engine.Server.Engine
{
    private readonly ConcurrentDictionary<SeasonKind, IRankSeason> _seasons = new();
    private readonly MySqlDbConnector _dbConnector = new();
    
    public RankServer()
    {
        Instance = this;
    }

    public static RankServer Instance { get; private set; } = null!;
    public RankRepository Repository { get; private set; } = null!;

    public bool Initialize(AppOptions options)
    {
        var config = new EngineConfigBuilder()
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
            .AddListener(new ListenerConfig { Ip = "Any", Port = options.Server.Port })
            .Build();

        if (!base.Initialize(options.Server.Name, config))
            return false;

        foreach (var database in options.Database)
        {
            if (!Enum.TryParse(database.Kind, true, out DbKind kind) ||
                string.IsNullOrEmpty(database.ConnectionString))
                return false;
        
            _dbConnector.Add(kind, database.ConnectionString);
        }

        Repository = new RankRepository(_dbConnector);
        
        CreateDailyRankSeason();
        return true;
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

    protected override bool TryCreatePeer(ISession session, out IPeer peer)
    {
        peer = new BaseServerPeer(session);
        return true;
    }

    protected override bool TryCreateConnector(string name, out IConnector connector)
    {
        throw new NotImplementedException();
    }

    public bool RegisterPeer(BaseServerPeer peer)
    {
        return AddOrUpdatePeer(peer);
    }

    public bool TryGetSeason(SeasonKind kind, out IRankSeason? season)
    {
        return _seasons.TryGetValue(kind, out season);
    }
}
