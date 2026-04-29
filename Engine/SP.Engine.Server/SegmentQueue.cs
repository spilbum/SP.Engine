using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;

namespace SP.Engine.Server;

public sealed class SegmentQueue(ArraySegment<byte>[] globalQueue, int baseOffset, int capacity)
    : IList<ArraySegment<byte>>
{
    private static readonly ArraySegment<byte> Null = default;
    private int _writeIndex;
    private int _readIndex;
    private bool _enqueueBlocked;
    private int _pendingWriteCount;

    public ushort TrackId { get; private set; } = 1;
    public int Count => _writeIndex - _readIndex;

    public ArraySegment<byte> this[int index]
    {
        get => globalQueue[baseOffset + _readIndex + index];
        set => throw new NotSupportedException("SegmentQueue is read-only for external indexer access");
    }
    
    public IEnumerator<ArraySegment<byte>> GetEnumerator()
    {
        for (var i = 0; i < Count; i++)
            yield return this[i];
    }
    
    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    public void Clear()
    {
        // 재사용 시 유효성 검증을 위한 ID 갱신
        TrackId = (ushort)(TrackId == ushort.MaxValue ? 1 : TrackId + 1);

        // 공유 배열의 참조를 해제하여 GC가 실제 데이터(byte[])를 수거하게 함
        for (var i = 0; i < _writeIndex; i++)
            globalQueue[baseOffset + i] = Null;

        _writeIndex = 0;
        _readIndex = 0;
    }

    public bool Enqueue(ArraySegment<byte> item, ushort trackId)
    {
        if (_enqueueBlocked) return false;   

        // 진행 중인 쓰기 작업 카운팅
        Interlocked.Increment(ref _pendingWriteCount);
        try
        {
            while (!_enqueueBlocked)
            {
                var curPos = _writeIndex;

                if (curPos >= capacity || trackId != TrackId) return false;

                if (Interlocked.CompareExchange(ref _writeIndex, curPos + 1, curPos) == curPos)
                {
                    globalQueue[baseOffset + curPos] = item;
                    return true;
                }

                // 경합 시 잠시 양보
                Thread.Yield();
            }

            return false;
        }
        finally
        {
            Interlocked.Decrement(ref _pendingWriteCount);
        }
    }

    public void TrimSentBytes(int sentByteCount)
    {
        if (sentByteCount <= 0 || Count <= 0) return;

        var accumulated = 0;
        for (var i = _readIndex; i < _writeIndex; i++)
        {
            var segment = globalQueue[baseOffset + i];
            accumulated += segment.Count;

            if (accumulated <= sentByteCount)
            {
                // 세그먼트 전체가 송신됨
                _readIndex = i + 1;
                continue;
            }

            // 세그먼트 일부가 송신됨: 남은 영역 재구성
            var excess = accumulated - sentByteCount;
            _readIndex = i;

            globalQueue[baseOffset + i] = new ArraySegment<byte>(
                segment.Array!,
                segment.Offset + (segment.Count - excess),
                excess
            );

            return;
        }

        // 모든 데이터 송신 완료 시 상태 초기화
        Clear();
    }
    
    public void StartEnqueue() => _enqueueBlocked = false;
    
    bool ICollection<ArraySegment<byte>>.IsReadOnly => true;
    void ICollection<ArraySegment<byte>>.Add(ArraySegment<byte> item) => throw new NotSupportedException();
    void ICollection<ArraySegment<byte>>.Clear() => Clear();
    bool ICollection<ArraySegment<byte>>.Contains(ArraySegment<byte> item) => throw new NotSupportedException();

    void ICollection<ArraySegment<byte>>.CopyTo(ArraySegment<byte>[] array, int arrayIndex)
    {
        for (var i = 0; i < Count; i++)
            array[arrayIndex + i] = this[i];
    }
    
    bool ICollection<ArraySegment<byte>>.Remove(ArraySegment<byte> item) => throw new NotSupportedException();
    int IList<ArraySegment<byte>>.IndexOf(ArraySegment<byte> item) => throw new NotSupportedException();
    void IList<ArraySegment<byte>>.Insert(int index, ArraySegment<byte> item) => throw new NotSupportedException();
    void IList<ArraySegment<byte>>.RemoveAt(int index) => throw new NotSupportedException();
}

