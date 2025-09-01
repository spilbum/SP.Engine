using System.Collections;

namespace SP.Rank;

public class MemoryRankCache<TKey, TEntry, TComparer>
    where TKey : notnull 
    where TEntry : IRankEntry<TKey> 
    where TComparer : IRankEntryComparer<TEntry>, new()
{
    private sealed class RecordChunk(TComparer comparer) : IEnumerable<TEntry>
    {
        private readonly SortedSet<TEntry> _records = new(comparer);
        public int Count => _records.Count;
        public TEntry Top => _records.Count == 0 ? throw new InvalidOperationException("Chunk empty") : _records.Min!;
        public TEntry Bottom => _records.Count == 0 ? throw new InvalidOperationException("Chunk empty") :  _records.Max!;
        public bool Add(TEntry record)
            => _records.Add(record);
        public bool Remove(TEntry record)
            => _records.Remove(record);

        public IEnumerator<TEntry> GetEnumerator()
            => _records.GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator()
            => GetEnumerator();
    }

    private sealed class OutOfRankCache(int capacity)
    {
        private readonly int _capacity = Math.Max(0, capacity);
        private readonly Dictionary<TKey, LinkedListNode<(TKey key, TEntry record)>> _map = new();
        private readonly LinkedList<(TKey key, TEntry record)> _cache = new();
        
        public int Count => _map.Count;

        public void Upsert(TKey key, TEntry record)
        {
            if (_capacity == 0) return;

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

        public bool TryPeek(TKey key, out TEntry? record)
        {
            if (_map.TryGetValue(key, out var node))
            {
                record = node.Value.record;
                return true;
            }

            record = default;
            return false;
        }

        public bool TryRemove(TKey key, out TEntry? record)
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
    
    private sealed class RecordLoc(RecordChunk chunk, TEntry record)
    {
        public RecordChunk Chunk = chunk;
        public readonly TEntry Record = record;
    }
    
    private readonly List<RecordChunk> _chunks = new();
    private readonly Dictionary<TKey, RecordLoc> _records = new();
    private readonly OutOfRankCache? _oorCache;
    private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.NoRecursion);
    private readonly TComparer _comparer;
    private readonly int _capacity;
    private readonly int _chunkSize;
    
    public MemoryRankCache(int capacity, int chunkSize, int? oorCapacity = null)
    {
        if (capacity <= 0) throw new ArgumentException("Invalid capacity");
        if (chunkSize <= 0) throw new ArgumentException("Invalid chunk size");

        _comparer = new TComparer();
        _capacity = capacity;
        _chunkSize = chunkSize;
        if (oorCapacity.HasValue)
            _oorCache = new OutOfRankCache(oorCapacity.Value);
    }

    public int Count
    {
        get
        {
            _lock.EnterReadLock();
            try { return _records.Count; }
            finally { _lock.ExitReadLock(); }
        }
    }

    public int TotalCount
    {
        get
        {
            _lock.EnterReadLock();
            try { return _records.Count + _oorCache?.Count ?? 0; }
            finally { _lock.ExitReadLock(); }
        }
    }

    public bool TryGetRecord(TKey key, out TEntry? record)
    {
        _lock.EnterReadLock();
        try
        {
            if (_records.TryGetValue(key, out var loc))
            {
                record = loc.Record;
                return true;
            }

            if (_oorCache != null)
            {
                if (_oorCache.TryPeek(key, out record))
                    return true;
            }

            record = default;
            return false;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }
    
    public void UpdateRecord(TEntry? record)
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

    public bool RemoveRecord(TKey key, out TEntry? record)
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
                _oorCache?.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
    
    private bool TryRemove(TKey key, out TEntry? record)
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

        if (_oorCache != null)
        {
            if (_oorCache.TryRemove(key, out record))
                return true;
        }

        record = default;
        return false;
    }
    
    public List<(TEntry record, int rank)> GetTop(int count)
    {
        if (count <= 0) 
            return [];
        
        _lock.EnterReadLock();
        try
        {
            var cap = Math.Min(count, _records.Count);
            var result = new List<(TEntry record, int rank)>(cap);
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
    
    public List<(TEntry record, int rank)> GetRange(int startRank, int count)
    {
        if (startRank <= 0 || count <= 0) 
            return [];
        
        _lock.EnterReadLock();
        try
        {
            var available = _records.Count;
            if (startRank > available) return [];

            var maxReach = available - (startRank - 1);
            var cap = Math.Min(count, Math.Max(0, maxReach));
            
            var result = new List<(TEntry record, int rank)>(cap);
            var curRank = 1;
            var endRank = startRank + count - 1;
            foreach (var chunk in _chunks)
            {
                foreach (var record in chunk)
                {
                    if (curRank > endRank) break;
                    if (curRank >= startRank) result.Add((record, curRank));
                    curRank++;
                }

                if (curRank > endRank) break;
            }
            return result;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public List<TInfo> GetTopInfos<TInfo>(int count, IRankFormatter<TEntry, TInfo> formatter)
    {
        var raw = GetTop(count);
        var result = new List<TInfo>(raw.Count);
        foreach (var (record, rank) in raw)
            result.Add(formatter.Format(record, rank));
        return result;
    }

    public List<TInfo> GetRangeInfos<TInfo>(int startRank, int count, IRankFormatter<TEntry, TInfo> formatter)
    {
        var raw = GetRange(startRank, count);
        var result = new List<TInfo>(raw.Count);
        foreach (var (record, rank) in raw)
            result.Add(formatter.Format(record, rank));
        return result;
    }
    
    private int BinarySearchChunkIndex(TEntry record)
    {
        if (_chunks.Count == 0 || _chunks is [{ Count: 0 }]) return 0;

        int low = 0, high = _chunks.Count - 1;
        while (low <= high)
        {
            var mid = (low + high) >> 1;
            if (_chunks[mid].Count == 0)
                return mid;

            var top = _chunks[mid].Top;
            var bottom = _chunks[mid].Bottom;
            
            var cmpTop = _comparer.Compare(record, top);
            var cmpBottom = _comparer.Compare(record, bottom);
            if (cmpTop < 0)
                high = mid - 1; // record 가 mid.Top 보다 상위 -> 왼쪽 청크
            else if (cmpBottom > 0)
                low = mid + 1; // record가 mid.Bottom 보다 하위 -> 오른쪽 청크 
            else
            {
                // top <= record <= bottom -> 해당 청크
                return mid;
            }
        }

        // 경계 보정 처리
        var index = Math.Clamp(low, 0, _chunks.Count - 1);
        while (index > 0 && _comparer.Compare(record, _chunks[index].Top) < 0) 
            index--;
        
        while (index < _chunks.Count - 1 && _comparer.Compare(record, _chunks[index].Bottom) > 0) 
            index++;
        
        return index;
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
        while (_records.Count > _capacity)
        {
            var lastIdx = _chunks.Count - 1;
            if (lastIdx < 0) return;
        
            var chunk = _chunks[lastIdx];
            var rec = chunk.Bottom;
            
            chunk.Remove(rec);
            _records.Remove(rec.Key);

            if (chunk.Count == 0)
                RemoveChunk(chunk);

            _oorCache?.Upsert(rec.Key, rec);
        }
    }
    
    private void SetLoc(TKey key, RecordChunk chunk, TEntry record)
    {
        _records[key] = new RecordLoc(chunk, record);
    }
}
