using System.Collections;

namespace SP.Shared.Rank;

public class RuntimeRankCache<TKey, TRecord>
    where TKey : notnull
    where TRecord : IRankRecord<TKey>
{
    private readonly List<RecordChunk> _chunks = [];
    private readonly int _chunkSize;
    private readonly IComparer<TRecord> _comparer;
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly OutOfRankCache? _outOfRankCache;
    private readonly int _rankedCapacity;
    private readonly Dictionary<TKey, RecordLoc> _records = new();

    public RuntimeRankCache(int rankedCapacity, int chunkSize, float outOfRankRatio, IComparer<TRecord> comparer)
    {
        if (rankedCapacity <= 0) throw new ArgumentException("Invalid capacity");
        if (chunkSize <= 0) throw new ArgumentException("Invalid divide count");

        _comparer = comparer ?? throw new ArgumentNullException(nameof(comparer));
        _rankedCapacity = rankedCapacity;
        _chunkSize = chunkSize;
        if (outOfRankRatio > 0.0)
            _outOfRankCache = new OutOfRankCache((int)(rankedCapacity * outOfRankRatio));
    }

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _records.Count;
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    public int TotalCount
    {
        get
        {
            _lock.EnterReadLock();
            try
            {
                return _records.Count + (_outOfRankCache?.Count ?? 0);
            }
            finally
            {
                _lock.ExitReadLock();
            }
        }
    }

    public bool TryGetRecord(TKey key, out TRecord? record)
    {
        _lock.EnterReadLock();
        try
        {
            if (_records.TryGetValue(key, out var loc))
            {
                record = loc.Record;
                return true;
            }

            if (_outOfRankCache != null)
                if (_outOfRankCache.TryPeek(key, out record))
                    return true;

            record = default;
            return false;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void UpdateRecord(TRecord? record)
    {
        if (record is null) return;

        _lock.EnterWriteLock();
        try
        {
            var key = record.Key;
            TryRemove(key, out _);

            EnsureFirstChunk();

            var idx = BinarySearchChunkIndex(record);
            var chunk = _chunks[idx];

            chunk.Add(record);
            SetLoc(key, chunk, record);

            if (chunk.Count > _chunkSize)
                SplitChunkAt(idx);

            TrimToCapacity();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void UpdateRecords(IEnumerable<TRecord> records)
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (var record in records)
            {
                var key = record.Key;
                TryRemove(key, out _);

                EnsureFirstChunk();

                var idx = BinarySearchChunkIndex(record);
                var chunk = _chunks[idx];
                chunk.Add(record);
                SetLoc(key, chunk, record);

                if (chunk.Count > _chunkSize)
                    SplitChunkAt(idx);
            }

            TrimToCapacity();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public bool RemoveRecord(TKey key, out TRecord? record)
    {
        _lock.EnterWriteLock();
        try
        {
            return TryRemove(key, out record);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void Clear(bool includeOutOfRank = true)
    {
        _lock.EnterWriteLock();
        try
        {
            _records.Clear();
            _chunks.Clear();

            if (includeOutOfRank)
                _outOfRankCache?.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private bool TryRemove(TKey key, out TRecord? record)
    {
        if (_records.TryGetValue(key, out var loc))
        {
            var chunk = loc.Chunk;
            if (chunk.Remove(loc.Record) && chunk.Count == 0)
                RemoveChunk(chunk);

            _records.Remove(key);
            record = loc.Record;
            return true;
        }

        if (_outOfRankCache != null)
            if (_outOfRankCache.TryRemove(key, out record))
                return true;

        record = default;
        return false;
    }

    public List<(TRecord record, int rank)> GetTop(int count)
    {
        if (count <= 0)
            return [];

        _lock.EnterReadLock();
        try
        {
            var cap = Math.Min(count, _records.Count);
            var result = new List<(TRecord record, int rank)>(cap);
            var need = cap;
            var rank = 1;

            foreach (var chunk in _chunks)
            {
                foreach (var record in chunk)
                {
                    if (need-- <= 0) break;
                    result.Add((record, rank));
                    rank++;
                }

                if (need <= 0) break;
            }

            return result;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool TryGetRank(TKey key, out int rank)
    {
        rank = 0;

        _lock.EnterReadLock();
        try
        {
            if (!_records.TryGetValue(key, out var loc))
                return false;

            // 앞선 chunk의 누계
            var prior = 0;
            foreach (var chunk in _chunks)
            {
                if (ReferenceEquals(chunk, loc.Chunk)) break;
                prior += chunk.Count;
            }

            // 해당 chunk 내 위치
            var idx = 0;
            foreach (var record in loc.Chunk)
            {
                if (ReferenceEquals(record, loc.Record))
                {
                    rank = prior + idx + 1;
                    return true;
                }

                idx++;
            }

            return false;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public List<(TRecord record, int rank)> GetAround(TKey key, int above, int below)
    {
        if (above < 0) above = 0;
        if (below < 0) below = 0;

        if (!TryGetRank(key, out var myRank))
            return [];

        var start = Math.Max(1, myRank - above);
        var count = above + below + 1;
        return GetRange(start, count);
    }

    public List<(TRecord record, int rank)> GetRange(int startRank, int count)
    {
        if (startRank <= 0 || count <= 0) return [];

        _lock.EnterReadLock();
        try
        {
            var available = _records.Count;
            if (startRank > available) return [];

            var endRank = Math.Min(available, startRank + count - 1);

            var result = new List<(TRecord record, int rank)>(endRank - startRank + 1);
            var rankBase = 1;
            var targetStart = startRank;
            var targetEnd = endRank;

            foreach (var chunk in _chunks)
            {
                var nextBase = rankBase + chunk.Count - 1;
                if (nextBase < targetStart)
                {
                    // 해당 chunk를 건너뜀
                    rankBase = nextBase + 1;
                    continue;
                }

                // 해당 chunk 안 랭크 범위
                var localStart = Math.Max(0, targetStart - rankBase);
                var localEnd = Math.Min(chunk.Count - 1, targetEnd - rankBase);

                if (localStart <= localEnd)
                {
                    var idx = 0;
                    foreach (var record in chunk)
                    {
                        if (idx > localEnd) break;
                        if (idx >= localStart)
                        {
                            var globalRank = rankBase + idx;
                            result.Add((record, globalRank));
                        }

                        idx++;
                    }
                }

                rankBase = nextBase + 1;
                if (rankBase > targetEnd) break;
            }

            return result;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public List<TInfo> GetTopInfos<TInfo>(int count, IRankFormatter<TRecord, TInfo> formatter)
    {
        var raw = GetTop(count);
        var result = new List<TInfo>(raw.Count);
        foreach (var (record, rank) in raw)
            result.Add(formatter.Format(record, rank));
        return result;
    }

    public List<TInfo> GetRangeInfos<TInfo>(int startRank, int count, IRankFormatter<TRecord, TInfo> formatter)
    {
        var raw = GetRange(startRank, count);
        var result = new List<TInfo>(raw.Count);
        foreach (var (record, rank) in raw)
            result.Add(formatter.Format(record, rank));
        return result;
    }

    private int BinarySearchChunkIndex(TRecord record)
    {
        if (_chunks.Count == 0 || (_chunks.Count == 1 && _chunks[0].Count == 0))
            return 0;

        int low = 0, high = _chunks.Count - 1;

        while (low <= high)
        {
            var mid = (low + high) >> 1;
            var chunk = _chunks[mid];
            if (chunk.Count == 0)
            {
                high = mid - 1;
                continue;
            }

            var top = chunk.Top;
            var bottom = chunk.Bottom;

            var compareTop = _comparer.Compare(record, top);
            var compareBottom = _comparer.Compare(record, bottom);

            if (compareTop < 0)
                high = mid - 1; // record 가 mid.Top 보다 상위 -> 왼쪽 청크
            else if (compareBottom > 0)
                low = mid + 1; // record가 mid.Bottom 보다 하위 -> 오른쪽 청크 
            else
                // top <= record <= bottom -> 해당 청크
                return mid;
        }

        if (low <= 0) return 0;
        if (low >= _chunks.Count) return _chunks.Count - 1;
        return low;
    }

    private void EnsureFirstChunk()
    {
        if (_chunks.Count != 0) return;
        _chunks.Add(new RecordChunk(_comparer));
    }

    private void SplitChunkAt(int idx)
    {
        var topChunk = _chunks[idx];
        if (topChunk.Count < 2) return;

        var bottomChunk = new RecordChunk(_comparer);

        var moveCnt = topChunk.Count / 2;
        var skipCnt = topChunk.Count - moveCnt;

        foreach (var rec in topChunk.Skip(skipCnt).ToList())
        {
            topChunk.Remove(rec);
            bottomChunk.Add(rec);
            if (_records.TryGetValue(rec.Key, out var loc))
                loc.Chunk = bottomChunk;
        }

        _chunks.Insert(idx + 1, bottomChunk);
    }

    private void RemoveChunk(RecordChunk chunk)
    {
        var idx = _chunks.IndexOf(chunk);
        if (idx >= 0) _chunks.RemoveAt(idx);
    }

    private void TrimToCapacity()
    {
        // 순위 밖 레코드 제거 및 이동
        while (_records.Count > _rankedCapacity)
        {
            var lastIdx = _chunks.Count - 1;
            if (lastIdx < 0) return;

            var chunk = _chunks[lastIdx];
            var rec = chunk.Bottom;

            chunk.Remove(rec);
            _records.Remove(rec.Key);

            if (chunk.Count == 0)
                RemoveChunk(chunk);

            _outOfRankCache?.Upsert(rec.Key, rec);
        }
    }

    private void SetLoc(TKey key, RecordChunk chunk, TRecord record)
    {
        _records[key] = new RecordLoc(chunk, record);
    }

    private sealed class RecordChunk(IComparer<TRecord> comparer) : IEnumerable<TRecord>
    {
        private readonly SortedSet<TRecord> _records = new(comparer);
        public int Count => _records.Count;
        public TRecord Top => _records.Count == 0 ? throw new InvalidOperationException("Chunk empty") : _records.Min!;

        public TRecord Bottom =>
            _records.Count == 0 ? throw new InvalidOperationException("Chunk empty") : _records.Max!;

        public IEnumerator<TRecord> GetEnumerator()
        {
            return _records.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }

        public bool Add(TRecord record)
        {
            return _records.Add(record);
        }

        public bool Remove(TRecord record)
        {
            return _records.Remove(record);
        }
    }

    private sealed class OutOfRankCache(int capacity)
    {
        private readonly LinkedList<(TKey key, TRecord record)> _cache = new();
        private readonly int _capacity = Math.Max(0, capacity);
        private readonly Dictionary<TKey, LinkedListNode<(TKey key, TRecord record)>> _map = new();

        public int Count => _map.Count;

        public void Upsert(TKey key, TRecord record)
        {
            if (_capacity == 0)
                return;

            if (_map.TryGetValue(key, out var node))
            {
                node.Value = (key, record);
                _cache.Remove(node);
                _cache.AddFirst(node);
            }
            else
            {
                var newNode = _cache.AddFirst((key, record));
                _map[key] = newNode;
            }

            Trim();
        }

        public bool TryPeek(TKey key, out TRecord? record)
        {
            if (_map.TryGetValue(key, out var node))
            {
                record = node.Value.record;
                return true;
            }

            record = default;
            return false;
        }

        public bool TryRemove(TKey key, out TRecord? record)
        {
            if (_map.TryGetValue(key, out var node))
            {
                _cache.Remove(node);
                _map.Remove(key);
                record = node.Value.record;
                return true;
            }

            record = default;
            return false;
        }

        private void Trim()
        {
            while (_map.Count > _capacity)
            {
                var tail = _cache.Last;
                if (tail is null) break;
                _cache.RemoveLast();
                _map.Remove(tail.Value.key);
            }
        }

        public void Clear()
        {
            _map.Clear();
            _cache.Clear();
        }
    }

    private sealed class RecordLoc(RecordChunk chunk, TRecord record)
    {
        public readonly TRecord Record = record;
        public RecordChunk Chunk = chunk;
    }
}
