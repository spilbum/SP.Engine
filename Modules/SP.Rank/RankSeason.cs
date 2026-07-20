using System.Collections.Concurrent;

namespace SP.Rank;

internal class ReverseComparer<T>(IComparer<T> inner) : IComparer<T>
{
    public int Compare(T? x, T? y)
    {
        return inner.Compare(y, x);
    }
}

public sealed class RankSeason<TKey, TRecord, TComparer>(string name) : IDisposable
    where TKey : IComparable<TKey>
    where TRecord : IRankRecord<TKey>
    where TComparer : RankRecordComparer<TKey, TRecord>, new()
{
    private readonly ConcurrentQueue<TRecord> _updateQueue = new();
    private List<TRecord>? _batchQueue;
    private MemoryRankBoard<TKey, TRecord>? _board;

    private bool _disposed;
    private int _initialized;
    private int _pendingStateValue = -1;
    private bool _running;
    private int _stateChanging;
    private int _stateValue;

    public string Name { get; } = name;
    public int SeasonNum { get; private set; }
    public DateTimeOffset StartUtc { get; private set; }
    public DateTimeOffset EndUtc { get; private set; }
    
    public int StateValue => Volatile.Read(ref _stateValue);
    public int Count => _board?.Count ?? 0;
    public int TotalCount => _board?.TotalCount ?? 0;

    public Action<int>? OnStateEnter { get; set; }
    public Action<int>? OnStateExit { get; set; }
    public Action<List<TRecord>>? OnRecordUpdated { get; set; }
    public Action<DateTimeOffset>? OnLogicTick { get; set; }

    public event EventHandler<ErrorEventArgs>? Error;

    public void Initialize(RankOptions options)
    {
        if (Interlocked.Exchange(ref _initialized, 1) == 1)
            throw new InvalidOperationException($"[{Name}] Already initialized.");
        
        var baseComparer = new TComparer();
        IComparer<TRecord> comparer = options.RankOrder == RankOrder.HigherIsBetter
            ? baseComparer
            : new ReverseComparer<TRecord>(baseComparer);

        _board = new MemoryRankBoard<TKey, TRecord>(
            options.MaxRankedCount,
            options.ChunkSize,
            options.OutOfRankRatio,
            comparer);

        _batchQueue = new List<TRecord>(options.RecordUpdatesPerTick);
    }

    public void Start()
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
        if (_disposed) throw new ObjectDisposedException(Name);
        Volatile.Write(ref _running, true);
    }

    public void UpdateSeasonInfo(int seasonNum, int state, DateTimeOffset startUtc, DateTimeOffset endUtc)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(seasonNum);
        if (endUtc <= startUtc) throw new ArgumentException("EndUtc must be after StartUtc");
        
        SeasonNum = seasonNum;
        StartUtc = startUtc;
        EndUtc = endUtc;
        Volatile.Write(ref _stateValue, state);
    }

    public bool Enqueue(TRecord record)
    {
        if (_disposed || !Volatile.Read(ref _running)) return false;
        _updateQueue.Enqueue(record);
        return true;
    }

    public bool RequestState(int state)
    {
        if (_disposed || _stateValue == state) return false;
        return Interlocked.CompareExchange(ref _pendingStateValue, state, -1) == -1;
    }

    public void ProcessLogicTick(DateTimeOffset now)
    {
        if (_disposed || !Volatile.Read(ref _running)) return;
        try
        {
            FlushPendingState();
            OnLogicTick?.Invoke(now);
            FlushPendingState();
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
    }

    public void ProcessBatchUpdate()
    {
        if (_disposed || !Volatile.Read(ref _running)) return;
        if (_updateQueue.IsEmpty) return;

        try
        {
            _batchQueue!.Clear();
            var count = 0;
            while (count++ < _batchQueue.Capacity && _updateQueue.TryDequeue(out var record))
            {
                _batchQueue.Add(record);
            }

            if (_batchQueue.Count == 0) return;

            _board!.UpdateRecords(_batchQueue);
            OnRecordUpdated?.Invoke(_batchQueue);
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
    }

    private void FlushPendingState()
    {
        var next = Interlocked.Exchange(ref _pendingStateValue, -1);
        if (next == -1) return;

        var state = _stateValue;
        var prev = Interlocked.CompareExchange(ref _stateValue, next, state);
        if (prev != state) return;

        if (Interlocked.Exchange(ref _stateChanging, 1) == 1) return;
        try
        {
            OnStateExit?.Invoke(prev);
            OnStateEnter?.Invoke(next);
        }
        catch (Exception ex)
        {
            OnError(ex);
        }
        finally
        {
            Volatile.Write(ref _stateChanging, 0);
        }
    }

    private void OnError(Exception ex)
    {
        Error?.Invoke(this, new ErrorEventArgs(ex));
    }

    public bool TryGetRecord(TKey key, out TRecord? record)
    {
        record = default;
        return _board?.TryGetRecord(key, out record) ?? false;
    }

    public bool TryRemoveRecord(TKey key, out TRecord? record)
    {
        record = default;
        return _board?.RemoveRecord(key, out record) ?? false;
    }

    public bool TryGetRank(TKey key, out int rank)
    {
        rank = 0;
        return _board?.TryGetRank(key, out rank) ?? false;
    }

    public TInfo? GetInfo<TInfo>(TKey key, IRankFormatter<TKey, TRecord, TInfo> formatter)
    {
        if (_disposed) return default;
        if (!TryGetRank(key, out var rank)) return default;
        return !TryGetRecord(key, out var record) ? default : formatter.Format(record!, rank);
    }

    public List<TInfo> GetTop<TInfo>(int count, IRankFormatter<TKey, TRecord, TInfo> formatter)
    {
        return _board?.GetTopInfos(count, formatter) ?? [];
    }

    public List<TInfo> GetRange<TInfo>(int startRank, int count, IRankFormatter<TKey, TRecord, TInfo> formatter)
    {
        return _board?.GetRangeInfos(startRank, count, formatter) ?? [];
    }

    public void Clear(bool includeOutOfRank = true)
    {
        _board?.Clear(includeOutOfRank);
    }

    public void Dispose()
    {
        if (_disposed) return;
        while (_updateQueue.TryDequeue(out _)) { }
        _board?.Clear();
        _board = null;
        _disposed = true;
    }
}
