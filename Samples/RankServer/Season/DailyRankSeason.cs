using System.Collections.Concurrent;
using Common;
using SP.Core.Logging;
using SP.Engine.Server.Logging;
using DatabaseHandler;
using SP.Shared.Rank.Season;

namespace RankServer.Season;

public interface IRankSeason : IDisposable
{
    int SeasonNum { get; }
    SeasonKind Kind { get; }
    SeasonState State { get; }
    DateTimeOffset StartUtc { get; }
    DateTimeOffset EndUtc { get; }

    void Start();
    void Pause();
    void Resume();
}

public class DailyRankSeason : BaseRankSeason<long, PlayerRankRecord, PlayerScoreDescComparer>, IRankSeason
{
    private static readonly TimeSpan BreakDuration = TimeSpan.FromHours(2);
    private readonly PlayerRankFormatter _formatter = new();
    private readonly ConcurrentDictionary<long, List<SeasonReward>> _rewards = new();

    public SeasonState State => (SeasonState)StateValue;
    
    public DailyRankSeason(ILogger logger)
        : base(logger, SeasonKind.Daily.ToString())
    {
        Error += OnError;
    }

    public SeasonKind Kind { get; } = SeasonKind.Daily;

    private void OnError(ErrorEventArgs e)
    {
        var ex = e.GetException();
        LogManager.Error(ex, "Season Error. kind={0}", Kind);
    }

    public override void Initialize(RankSeasonOptions options)
    {
        base.Initialize(options);

        if (!LoadOrBootstrapSeasonFromDb())
            throw new InvalidOperationException("Failed to load season from db");
    }

    public bool UpdateRecord(long uid, int deltaScore, string? name, string? countryCode)
    {
        PlayerRankRecord newRecord;
        if (TryGetRecord(uid, out var record))
        {
            var newScore = record!.Score + deltaScore;
            newRecord = record.WithScore(newScore, name, countryCode, DateTimeOffset.UtcNow);
        }
        else
        {
            newRecord = new PlayerRankRecord(uid, deltaScore, name, countryCode);
        }

        return Enqueue(newRecord);
    }

    public PlayerRankInfo? GetInfo(long uid)
    {
        return GetInfo(uid, _formatter);
    }

    public List<PlayerRankInfo> GetTopInfos(int count)
    {
        return GetTop(count, _formatter);
    }

    public List<PlayerRankInfo> GetRangeInfos(int startRank, int count)
    {
        return GetRange(startRank, count, _formatter);
    }

    public void ReceiveSeasonReward(long uid)
    {
        if (!_rewards.TryGetValue(uid, out var rewards) || rewards.Count == 0)
            return;

        try
        {
            var receivable = rewards.Where(r => !r.IsRewarded).ToList();
            foreach (var reward in receivable) 
                RankServer.Instance.Repository.ReceiveRankSeasonReward(Kind, reward.SeasonNum, uid);
            
            foreach (var reward in receivable) 
                reward.Received();
        }
        catch (Exception e)
        {
            LogManager.Error(e, "Season receive reward failed. uid={0}", uid);
        }
    }

    protected override void OnRecordUpdated(List<PlayerRankRecord> records)
    {
        try
        {
            foreach (var record in records)
                RankServer.Instance.Repository.UpsertRankSeasonRecord(new RankDb.RankSeasonRecordEntity
                {
                    SeasonKind = (byte)Kind,
                    SeasonNum = SeasonNum,
                    Uid = record.Uid,
                    Score = record.Score,
                    Name = record.Name,
                    CountryCode = record.CountryCode,
                    UpdatedUtc = record.UpdateUtc.UtcDateTime
                });
        }
        catch (Exception e)
        {
            LogManager.Error(e);
        }
    }

    protected override void OnTick(DateTimeOffset now)
    {
        switch (State)
        {
            case SeasonState.Scheduled:
                if (now >= StartUtc) RequestState((int)SeasonState.Running);
                break;
            case SeasonState.Running:
                if (now >= EndUtc) RequestState((int)SeasonState.Ending);
                break;
            case SeasonState.Ending:
            case SeasonState.Ended:
                break;
            case SeasonState.Break:
                if (now >= GetBreakEndUtc())
                    Scheduler.TryEnqueue(NewSeason, now);
                break;
            default:
                throw new Exception($"Invalid state: {State}");
        }
    }

    private void NewSeason(DateTimeOffset now)
    {
        var nextNum = SeasonNum + 1;
        var nextStart = AlignDayStartUtc(now);
        var nextEnd = nextStart.AddDays(1);

        SaveSeasonToDb(nextNum, SeasonState.Scheduled, nextStart, nextEnd);
        UpdateSeasonInfo(nextNum, (int)SeasonState.Scheduled, nextStart, nextEnd);

        OnExit((int)SeasonState.Break);
        OnEnter((int)SeasonState.Scheduled);

        LogManager.Info("[Daily] Next season scheduled. kind={0}, num={1}, start={2}, end={3}",
            Kind, nextNum, nextStart, nextEnd);
    }

    protected override void OnEnter(int state)
    {
        switch ((SeasonState)state)
        {
            case SeasonState.Ending:
                Scheduler.TryEnqueue(EndSeason);
                break;
            case SeasonState.Ended:
                RequestState((int)SeasonState.Break);
                break;
        }
        
        SaveCurrentSeason();
    }

    private void EndSeason()
    {
        try
        {
            RankServer.Instance.Repository.EndRankSeason(Kind, SeasonNum);
            
            var top10 = GetTopInfos(10);
            foreach (var record in top10)
            {
                var itemInfo = new ItemInfo();
                switch (record.Rank)
                {
                    case 1:
                        itemInfo.Kind = ItemKind.Coin;
                        itemInfo.Value = 100;
                        break;
                    case 2:
                        itemInfo.Kind = ItemKind.Coin;
                        itemInfo.Value = 70;
                        break;
                    case 3:
                        itemInfo.Kind = ItemKind.Coin;
                        itemInfo.Value = 50;
                        break;
                    case <= 5:
                        itemInfo.Kind = ItemKind.Coin;
                        itemInfo.Value = 30;
                        break;
                    default:
                        itemInfo.Kind = ItemKind.Coin;
                        itemInfo.Value = 10;
                        break;
                }

                var entity = new RankDb.RankSeasonRewardEntity
                {
                    SeasonKind = (byte)Kind,
                    SeasonNum = SeasonNum,
                    Uid = record.Uid,
                    Rank = record.Rank,
                    Score = record.Score,
                    IsRewarded = false,
                    ItemKind = (byte)itemInfo.Kind,
                    ItemId = itemInfo.ItemId,
                    ItemValue = itemInfo.Value
                };

                RankServer.Instance.Repository.InsertRankSeasonReward(entity);
            }

            LoadSeasonRewardsFromDb(Kind);
            Clear();
            RequestState((int)SeasonState.Ended);
        }
        catch (Exception e)
        {
            LogManager.Error(e);
        }
    }

    private void LoadSeasonRewardsFromDb(SeasonKind kind)
    {
        var entities = RankServer.Instance.Repository.GetAllRankSeasonRewards(kind);
        foreach (var entity in entities)
        {
            if (!_rewards.TryGetValue(entity.Uid, out var list))
            {
                list = [];
                _rewards[entity.Uid] = list;
            }

            list.Add(new SeasonReward(entity));
            LogManager.Debug(
                "[Daily] Reward loaded. season={0}/{1}, uid={2}, rank={3}, score={4}, isRewarded={5}, item=[{6}]",
                (SeasonKind)entity.SeasonKind, entity.SeasonNum, entity.Uid, entity.Rank, entity.Score,
                entity.IsRewarded,
                $"{entity.ItemKind}:{entity.ItemId}:{entity.ItemValue}");
        }
    }

    private void LoadSeasonRecordsFromDb(SeasonKind kind, int seasonNum)
    {
        var records = RankServer.Instance.Repository.GetAllRankSeasonRecords(kind, seasonNum);
        foreach (var record in records)
        {
            UpdateRecordToCache(new PlayerRankRecord(record.Uid, record.Score, record.Name, record.CountryCode,
                record.UpdatedUtc));
            LogManager.Debug(
                "[Daily] Record loaded. season={0}/{1}, uid={2}, score={3}, name={4}, countryCode={5}, updateUtc={6}",
                kind, seasonNum, record.Uid, record.Score, record.Name, record.CountryCode, record.UpdatedUtc);
        }
    }

    private bool LoadOrBootstrapSeasonFromDb()
    {
        var season = RankServer.Instance.Repository.GetLatestRankSeason(Kind);
        if (season == null)
        {
            var now = DateTimeOffset.UtcNow;
            var startUtc = AlignDayStartUtc(now);
            var endUtc = startUtc.AddDays(1);
            var initState =
                now < startUtc ? SeasonState.Scheduled :
                now < endUtc ? SeasonState.Running : SeasonState.Ending;

            SaveSeasonToDb(1, initState, startUtc, endUtc);
            UpdateSeasonInfo(1, (int)initState, startUtc, endUtc);
            OnEnter((int)initState);
            LogManager.Info("[Daily] Season bootstrap. kind={0}, num={1}, state={2}, start={3}, end={4}",
                Kind, 1, initState, startUtc, endUtc);
            return true;
        }

        var kind = (SeasonKind)season.Kind;
        var state = (SeasonState)season.State;

        LoadSeasonRecordsFromDb(kind, season.SeasonNum);
        LoadSeasonRewardsFromDb(kind);

        UpdateSeasonInfo(season.SeasonNum, (int)state, season.StartUtc, season.EndUtc);
        OnEnter((int)state);

        LogManager.Info("[Daily] Season loaded. kind={0}, num={1}, state={2}, start={3}, end={4}",
            Kind, SeasonNum, State, StartUtc, EndUtc);

        return true;
    }

    private void SaveCurrentSeason()
    {
        SaveSeasonToDb(SeasonNum, State, StartUtc, EndUtc);
    }

    private void SaveSeasonToDb(int seasonNum, SeasonState state, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        RankServer.Instance.Repository.UpsertRankSeason(new RankDb.RankSeasonEntity
        {
            Kind = (byte)Kind,
            SeasonNum = seasonNum,
            State = (byte)state,
            StartUtc = startUtc.UtcDateTime,
            EndUtc = endUtc.UtcDateTime
        });
    }

    private static DateTimeOffset AlignDayStartUtc(DateTimeOffset t)
    {
        return new DateTimeOffset(t.UtcDateTime.Date, TimeSpan.Zero);
    }

    private DateTimeOffset GetBreakEndUtc()
    {
        return EndUtc + BreakDuration;
    }

    private class SeasonReward
    {
        public SeasonReward(RankDb.RankSeasonRewardEntity entity)
        {
            SeasonNum = entity.SeasonNum;
            Uid = entity.Uid;
            Rank = entity.Rank;
            Score = entity.Score;
            IsRewarded = entity.IsRewarded;
            Reward = new ItemInfo
            {
                Kind = (ItemKind)entity.ItemKind,
                ItemId = entity.ItemId,
                Value = entity.ItemValue
            };
        }

        public int SeasonNum { get; }
        public long Uid { get; }
        public int Rank { get; }
        public int Score { get; }
        public bool IsRewarded { get; private set; }
        public ItemInfo Reward { get; }

        public void Received()
        {
            IsRewarded = true;
        }
    }
}
