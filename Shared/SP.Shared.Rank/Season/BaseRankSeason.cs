using System.Collections.Concurrent;
using SP.Core.Fiber;
using SP.Core.Logging;

namespace SP.Shared.Rank.Season;

public abstract class BaseRankSeason<TKey, TRecord, TComparer>(ILogger logger, string name) : IDisposable
    where TKey : notnull
    where TRecord : IRankRecord<TKey>
    where TComparer : BaseRankSeasonRecordComparer<TRecord>, new()
{
    private readonly ConcurrentQueue<TRecord> _updateQueue = new();
    private List<TRecord>? _batchQueue;

    private RuntimeRankCache<TKey, TRecord>? _cache;
    private bool _disposed;
    private int _initialized;
    private int _pendingStateValue = -1;
    private bool _running;
    private FiberScheduler? _scheduler;
    private int _stateChanging;
    private int _stateValue;
    public string Name { get; } = name;
    public int SeasonNum { get; private set; }
    public DateTimeOffset StartUtc { get; private set; }
    public DateTimeOffset EndUtc { get; private set; }
    
    protected int StateValue => Volatile.Read(ref _stateValue);
    protected IFiberScheduler Scheduler => _scheduler!;

    public int Count => _cache?.Count ?? 0;
    public int TotalCount => _cache?.TotalCount ?? 0;

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    public event Action<ErrorEventArgs>? Error;

    private void OnError(Exception e)
    {
        Error?.Invoke(new ErrorEventArgs(e));
    }

    protected void UpdateRecordToCache(TRecord record)
    {
        _cache?.UpdateRecord(record);
    }

    public virtual void Initialize(RankSeasonOptions options)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
            throw new InvalidOperationException("Already initialized");

        var baseComparer = new TComparer();
        IComparer<TRecord> comparer = options.RankOrder == RankOrder.HigherIsBetter
            ? baseComparer
            : new ReverseComparer<TRecord>(baseComparer);

        _cache = new RuntimeRankCache<TKey, TRecord>(
            options.RankedCapacity,
            options.ChunkSize,
            options.OutOfRankRatio,
            comparer);

        _batchQueue = new List<TRecord>(options.MaxUpdatesPerTick);

        _scheduler = new FiberScheduler(logger, "RankSeasonFiber");
        _scheduler.Schedule(Tick, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
        _scheduler.Schedule(WorkerTick, TimeSpan.Zero, TimeSpan.FromMilliseconds(50));
    }

    public virtual void Start()
    {
        if (_disposed) return;
        Volatile.Write(ref _running, true);
    }

    public void Pause()
    {
        Volatile.Write(ref _running, false);
    }

    public void Resume()
    {
        if (!_disposed)
            Volatile.Write(ref _running, true);
        else
            throw new ObjectDisposedException(Name);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (!disposing)
            return;

        _scheduler?.Dispose();
        while (_updateQueue.TryDequeue(out _)) { }
        _cache?.Clear();
        _cache = null;
        _disposed = true;
    }

    protected void UpdateSeasonInfo(int seasonNum, int state, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seasonNum);
        if (endUtc <= startUtc) throw new ArgumentException("EndUtc must be after StartUtc.");

        SeasonNum = seasonNum;
        StartUtc = startUtc;
        EndUtc = endUtc;
        Volatile.Write(ref _stateValue, state);
    }

    protected bool Enqueue(TRecord record)
    {
        if (_disposed) return false;
        if (!Volatile.Read(ref _running)) return false;
        _updateQueue.Enqueue(record);
        return true;
    }

    protected bool RequestState(int state)
    {
        if (_disposed) return false;
        if (_stateValue == state) return false;
        return Interlocked.CompareExchange(ref _pendingStateValue, state, -1) == -1;
    }

    private void FlushPendingState()
    {
        var next = Interlocked.Exchange(ref _pendingStateValue, -1);
        if (next == -1) return;

        var state = _stateValue;
        var prev = Interlocked.CompareExchange(ref _stateValue, next, state);
        if (prev != state)
            return;

        if (Interlocked.Exchange(ref _stateChanging, 1) == 1)
            return;
        try
        {
            OnExit(prev);
            OnEnter(next);
        }
        catch (Exception e)
        {
            OnError(e);
        }
        finally
        {
            Volatile.Write(ref _stateChanging, 0);
        }
    }

    private void Tick()
    {
        if (_disposed || !Volatile.Read(ref _running)) return;
        try
        {
            var now = DateTimeOffset.UtcNow;
            FlushPendingState();
            OnTick(now);
            FlushPendingState();
        }
        catch (Exception e)
        {
            OnError(e);
        }
    }

    private void WorkerTick()
    {
        if (_disposed || !Volatile.Read(ref _running)) return;
        if (_updateQueue.IsEmpty) return;

        try
        {
            _batchQueue!.Clear();
            var count = 0;
            while (count++ < _batchQueue.Capacity && _updateQueue.TryDequeue(out var record))
                _batchQueue.Add(record);

            if (_batchQueue.Count == 0) return;
            _cache!.UpdateRecords(_batchQueue);
            OnRecordUpdated(_batchQueue);
        }
        catch (Exception e)
        {
            OnError(e);
        }
    }

    protected virtual void OnRecordUpdated(List<TRecord> records)
    {
    }

    protected virtual void OnTick(DateTimeOffset now)
    {
    }

    protected virtual void OnEnter(int state)
    {
    }

    protected virtual void OnExit(int state)
    {
    }

    protected bool TryGetRecord(TKey key, out TRecord? record)
    {
        record = default;
        return _cache?.TryGetRecord(key, out record) ?? false;
    }

    protected bool TryRemoveRecord(TKey key, out TRecord? record)
    {
        record = default;
        return _cache?.RemoveRecord(key, out record) ?? false;
    }

    protected bool TryGetRank(TKey key, out int rank)
    {
        rank = 0;
        return _cache?.TryGetRank(key, out rank) ?? false;
    }

    protected TInfo? GetInfo<TInfo>(TKey key, IRankFormatter<TRecord, TInfo> formatter)
    {
        if (_disposed) return default;
        if (!TryGetRank(key, out var rank)) return default;
        return !TryGetRecord(key, out var record)
            ? default
            : formatter.Format(record!, rank);
    }

    protected List<TInfo> GetTop<TInfo>(int count, IRankFormatter<TRecord, TInfo> formatter)
    {
        return _cache?.GetTopInfos(count, formatter) ?? [];
    }

    protected List<TInfo> GetRange<TInfo>(int startRank, int count, IRankFormatter<TRecord, TInfo> formatter)
    {
        return _cache?.GetRangeInfos(startRank, count, formatter) ?? [];
    }

    protected void Clear(bool includeOutOfRank = true)
    {
        _cache?.Clear(includeOutOfRank);
    }
}
