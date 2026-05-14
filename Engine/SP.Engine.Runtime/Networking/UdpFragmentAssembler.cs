using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Threading;
using SP.Core;

namespace SP.Engine.Runtime.Networking
{
    internal sealed class FragmentState : IDisposable
    {
        private const int MaxAllowedFragments = 64;
        private readonly PooledBuffer[] _fragments;
        private readonly int[] _lengths;
        private int _totalLength;
        private int _disposed; // 0: alive, 1: disposed
        
        public readonly DateTime CreateAtUtc = DateTime.UtcNow;
        public int AddedCount { get; private set; }
        public byte TotalCount { get; }

        public FragmentState(byte count)
        {
            if (count > MaxAllowedFragments) throw new ArgumentOutOfRangeException(nameof(count));
            
            TotalCount = count;
            _fragments = ArrayPool<PooledBuffer>.Shared.Rent(count);
            _lengths = ArrayPool<int>.Shared.Rent(count);
            
            Array.Clear(_fragments, 0, count);
            Array.Clear(_lengths, 0, count);
        }

        public bool Add(byte index, ReadOnlySpan<byte> span)
        {
            if (Interlocked.CompareExchange(ref _disposed, 0, 0) != 0) return false;
            if (index >= TotalCount || _fragments[index] != null && _fragments[index].Capacity > 0) return false;

            var pooled = new PooledBuffer(span.Length);
            span.CopyTo(pooled.Memory.Span);

            _fragments[index] = pooled;
            _lengths[index] = span.Length;
            Interlocked.Add(ref _totalLength, span.Length);
            AddedCount++;
            return true;
        }

        public PooledBuffer Combine(out int length)
        {
            var result = new PooledBuffer(_totalLength);
            var dest = result.Memory.Span;
            var offset = 0;
            
            for (var i = 0; i < TotalCount; i++)
            {
                var len = _lengths[i];
                if (_fragments[i] == null) continue;
                
                var src = _fragments[i].Slice(0, len);
                src.CopyTo(dest[offset..]);
                offset += len;
            }
    
            length = offset;
            return result;
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            
            for (var i = 0; i < TotalCount; i++)
                _fragments[i]?.Dispose();

            ArrayPool<PooledBuffer>.Shared.Return(_fragments);
            ArrayPool<int>.Shared.Return(_lengths);
        }
    }
    
    public sealed class UdpFragmentAssembler : IDisposable
    {
        private readonly ConcurrentDictionary<uint, FragmentState> _assembles =
            new ConcurrentDictionary<uint, FragmentState>();

        private readonly int _cleanupTimeoutSec;
        private readonly int _maxPendingMessageCount;
        private bool _disposed;
        
        public UdpFragmentAssembler(int cleanupTimeoutSec, int maxPendingMessageCount)
        {
            _cleanupTimeoutSec = cleanupTimeoutSec;
            _maxPendingMessageCount = maxPendingMessageCount;
        }
        
        public void Cleanup(DateTime now)
        {
            if (_assembles.IsEmpty) return;

            var timeout = TimeSpan.FromSeconds(_cleanupTimeoutSec);
            
            foreach (var (key, state) in _assembles)
            {
                if (now - state.CreateAtUtc < timeout) continue;
                if (_assembles.TryRemove(key, out var expired))
                    expired.Dispose();
            }
        }

        public bool TryPush(UdpHeader header, ReadOnlySpan<byte> bodyData, out UdpMessage message)
        {
            message = null;
            if (_disposed) return false;

            if (!UdpFragmentHeader.TryRead(bodyData, out var fragHeader, out var fragHeaderConsumed)) return false;
            
            var fragData = bodyData[fragHeaderConsumed..];
                
            if (!_assembles.ContainsKey(fragHeader.FragId) && _assembles.Count >= _maxPendingMessageCount) 
                return false;
                
            if (!TryAssembleInternal(fragHeader, fragData, out var bodyOwner, out var bodyLength)) 
                return false;
                
            message = new UdpMessage(header, bodyOwner, bodyLength);
            return true;
        }
        
        private bool TryAssembleInternal(
            UdpFragmentHeader fragHeader,
            ReadOnlySpan<byte> fragData, 
            out IMemoryOwner<byte> bodyOwner, 
            out int bodyLength)
        {
            bodyOwner = null;
            bodyLength = 0;

            var state = _assembles.GetOrAdd(fragHeader.FragId, _ => new FragmentState(fragHeader.TotalCount));

            lock (state)
            {
                if (!state.Add(fragHeader.Index, fragData)) return false;

                if (state.AddedCount < state.TotalCount) return false;

                if (!_assembles.TryRemove(fragHeader.FragId, out _)) return true;
                
                using (state)
                {
                    bodyOwner = state.Combine(out var length);
                    bodyLength = length;
                    return true;
                }
            }
        }

        public void Dispose()
        {
            _disposed = true;
            foreach (var key in _assembles.Keys)
            {
                if (_assembles.TryRemove(key, out var state))
                    state.Dispose();
            }
        }
    }
}
    
