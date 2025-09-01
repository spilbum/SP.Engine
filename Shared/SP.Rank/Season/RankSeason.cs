using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using SP.Common.Fiber;
using SP.Common.Logging;
using SP.Database;

namespace SP.Rank.Season;

public interface IRankSeason : IDisposable
{
    string Name { get; }
    int SeasonId { get; }
    int SeasonNum { get; }
    DateTimeOffset StartTime { get; }
    DateTimeOffset EndTime { get; }
    RankSeasonState State { get; }

    bool Initialize(int seasonId, int seasonNum, DateTimeOffset startTime, DateTimeOffset endTime,
        RankSeasonState state, RankSeasonOptions options);
    void Start();
    void Stop();
}

public abstract class RankSeason<TEntry, TComparer> : IRankSeason
    where TEntry : RankSeasonEntry
    where TComparer : RankSeasonEntryComparer<TEntry>, new()
{
    private const int WorkerBatchCount = 100;

    public string Name { get; }
    public int SeasonId { get; private set; }
    public int SeasonNum { get; private set; }
    public DateTimeOffset StartTime { get; private set; }
    public DateTimeOffset EndTime { get; private set; }
    public RankSeasonState State => (RankSeasonState)Volatile.Read(ref _stateValue);

    private MemoryRankCache<long, TEntry, TComparer>? _cache;
    private ThreadFiber? _scheduler;
    private readonly List<ThreadFiber> _workers = new();
    private readonly ConcurrentQueue<TEntry> _updateQueue = new();
    private bool _running;
    private int _disposed;
    private int _initialized;
    private int _stateValue = (int)RankSeasonState.None;
    private int _pendingStateValue = -1;
    private int _inChangeState;
    private ILogger? _logger;
    private bool IsDisposed => Volatile.Read(ref _disposed) == 1;

    protected RankSeason(string name, ILogger? logger = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        _logger = logger;
    }

    public virtual bool Initialize(int seasonId, int seasonNum, DateTimeOffset startTime, DateTimeOffset endTime,
        RankSeasonState state, RankSeasonOptions options)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
            throw new InvalidOperationException("Already initialized");
        
        try
        {
            _cache = new MemoryRankCache<long, TEntry, TComparer>(
                capacity: options.RankStoreCapacity,
                chunkSize: options.RankStoreChunkSize,
                oorCapacity: options.OutOfRankCapacity);

            _scheduler = new ThreadFiber();
            _scheduler.Schedule(SchedulerTick, options.UpdaterIntervalMs, options.UpdaterIntervalMs);

            var workerCnt = options.WorkerCount > 0
                ? options.WorkerCount
                : Math.Max(1, Environment.ProcessorCount / 2);

            for (var i = 0; i < workerCnt; i++)
            {
                var worker = new ThreadFiber();
                worker.Schedule(WorkerTick, options.WorkerUpdateIntervalMs, options.WorkerUpdateIntervalMs);
                _workers.Add(worker);
            }

            SeasonId = seasonId;
            SeasonNum = seasonNum;
            StartTime = startTime.ToUniversalTime();
            EndTime = endTime.ToUniversalTime();
            if (EndTime <= StartTime)
                throw new ArgumentException("EndTime must be after StartTime (UTC).");

            Interlocked.Exchange(ref _stateValue, (int)state);
            OnEnter(state);
            return true;
        }
        catch (Exception e)
        {
            _logger?.Error(e);
            return false;
        }
    }

    public virtual void Start()
    {
        if (IsDisposed) return;
        
        if (_scheduler == null || _workers.Count == 0)
            throw new InvalidOperationException("Initialize must be called before Start.");

        if (Volatile.Read(ref _running)) return;
        
        _scheduler.Start();
        foreach (var w in _workers) w.Start();
        Volatile.Write(ref _running, true);
    }

    public void Stop()
    {
        Dispose();
    }

    protected bool TryUpdateSeasonInfo(int seasonNum, DateTimeOffset start, DateTimeOffset end)
    {
        if (IsDisposed) return false;
        if (State is not RankSeasonState.Scheduled and not RankSeasonState.Break) 
            return false;
        
        if (seasonNum <= 0) return false;
        
        var s = start.ToUniversalTime();
        var e = end.ToUniversalTime();
        if (e <= s) return false;
        
        SeasonNum = seasonNum;
        StartTime = s;
        EndTime = e;
        return true;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
        Volatile.Write(ref _running, false);
        
        _scheduler?.Dispose();
        _scheduler = null;
        
        foreach (var w in _workers) w.Dispose();
        _workers.Clear();
        
        while (_updateQueue.TryDequeue(out _)) {}
        _cache?.Clear();
        _cache = null;
        
        GC.SuppressFinalize(this);
    }

    public bool Enqueue(TEntry entry)
    {
        if (IsDisposed) return false;
        if (!Volatile.Read(ref _running)) return false;
        if (State != RankSeasonState.Running) return false;
        _updateQueue.Enqueue(entry);
        return true;
    }
    
    protected bool RequestStateChange(RankSeasonState state)
    {
        if (IsDisposed) return false;
        if (State == state) return false;
        return Interlocked.CompareExchange(ref _pendingStateValue, (int)state, -1) == -1;
    }
    
    private void ChangeState(RankSeasonState expected, RankSeasonState next)
    {
        var prev = (RankSeasonState)Interlocked.CompareExchange(ref _stateValue, (int)next, (int)expected);
        if (prev != expected)
        {
            _logger?.Warn("ChangeState refused: {0} -> {1}, expected={2}", prev, next, expected);
            return;
        }

        if (Interlocked.Exchange(ref _inChangeState, 1) == 1)
        {
            _logger?.Warn("Re-entrant state change suppressed: {0} -> {1}", expected, next);
            return;
        }

        try
        {
            OnExit(prev);
            OnEnter(next);
            OnStateChanged(prev, next);
        }
        catch (Exception e)
        {
            _logger?.Error(e);
        }
        finally
        {
            Volatile.Write(ref _inChangeState, 0);
        }
        
        _logger?.Debug("State changed: {0} -> {1}", prev, next);
    }

    private void SchedulerTick()
    {
        if (IsDisposed) return;
        if (!Volatile.Read(ref _running)) return;
        var now = DateTimeOffset.UtcNow;
        UpdateState();
        OnTick(now);
    }

    private void UpdateState()
    {
        var req = Interlocked.Exchange(ref _pendingStateValue, -1);
        if (req == -1) return;
        var cur = State;
        var next = (RankSeasonState)req;
        ChangeState(cur, next);
    }

    private void WorkerTick()
    {
        if (IsDisposed) return;
        if (!Volatile.Read(ref _running)) return;
        if (State != RankSeasonState.Running) return;
        
        var count = 0;
        while (count++ < WorkerBatchCount && _updateQueue.TryDequeue(out var record))
            _cache?.UpdateRecord(record);
    }

    protected virtual void OnTick(DateTimeOffset now)
    {
    }

    protected virtual void OnEnter(RankSeasonState state)
    {
        switch (state)
        {
            case RankSeasonState.Scheduled: OnEnterScheduled(); break;
            case RankSeasonState.Running: OnEnterRunning(); break;
            case RankSeasonState.Ending: OnEnterEnding(); break;
            case RankSeasonState.Ended: OnEnterEnded(); break;
            case RankSeasonState.Break: OnEnterBreak(); break;
        }
    }

    protected virtual void OnExit(RankSeasonState state)
    {
        switch (state)
        {
            case RankSeasonState.Scheduled: OnExitScheduled(); break;
            case RankSeasonState.Running: OnExitRunning(); break;
            case RankSeasonState.Ending: OnExitEnding(); break;
            case RankSeasonState.Ended: OnExitEnded(); break;
            case RankSeasonState.Break: OnExitBreak(); break;
        }
    }

    protected virtual void OnStateChanged(RankSeasonState prev, RankSeasonState next)
    {
    }
    
    protected virtual void OnEnterScheduled() {}
    protected virtual void OnExitScheduled() {}
    protected virtual void OnEnterRunning() {}
    protected virtual void OnExitRunning() {}
    protected virtual void OnEnterEnding() {}
    protected virtual void OnExitEnding() {}
    protected virtual void OnEnterEnded() {}
    protected virtual void OnExitEnded() {}
    protected virtual void OnEnterBreak() {}
    protected virtual void OnExitBreak() {}
    
    public bool TryGetRecord(long key, out TEntry? record)
    {
        record = null;
        return _cache?.TryGetRecord(key, out record) ?? false;
    }
    
    public bool TryRemoveRecord(long key, out TEntry? record)
    {
        record = null;
        return _cache?.RemoveRecord(key, out record) ?? false;
    }

    public List<(TEntry record, int rank)> GetTop(int count)
    {
        if (IsDisposed) return [];
        return _cache?.GetTop(count) ?? [];
    }

    public List<(TEntry record, int rank)> GetRange(int startRank, int count)
    {
        if (IsDisposed) return [];
        return _cache?.GetRange(startRank, count) ?? [];
    }

    public List<TInfo> GetTopInfo<TInfo>(int count, IRankFormatter<TEntry, TInfo> formatter)
    {
        if (IsDisposed) return [];
        return _cache?.GetTopInfos(count, formatter) ?? [];
    }

    public List<TInfo> GetRangeInfo<TInfo>(int startRank, int count, IRankFormatter<TEntry, TInfo> formatter)
    {
        if (IsDisposed) return [];
        return _cache?.GetRangeInfos(startRank, count, formatter) ?? [];
    }
    
    public void Clear(bool includeOutOfRank = true) => _cache?.Clear(includeOutOfRank);
    public int Count => IsDisposed ? 0 : _cache?.Count ?? 0;
    public int TotalCount => IsDisposed ? 0 : _cache?.TotalCount ?? 0;
}
