using System;
using System.Buffers;
using System.Collections.Concurrent;

namespace SP.Engine.Runtime.Networking
{
    internal sealed class FragmentReassembly : IDisposable
    {
        private readonly byte[][] _fragments;
        private readonly int[] _lengths;
        private int _totalSize;
        
        public readonly long CreateAt = DateTime.UtcNow.Ticks;
        public int ReceivedCount { get; private set; }
        public byte ExpectedCount { get; }

        public FragmentReassembly(byte count)
        {
            ExpectedCount = count;
            _fragments = ArrayPool<byte[]>.Shared.Rent(count);
            _lengths = ArrayPool<int>.Shared.Rent(count);
            Array.Clear(_fragments, 0, count);
        }

        public bool Add(byte index, ArraySegment<byte> segment)
        {
            if (index >= ExpectedCount || _fragments[index] != null)
                return false;

            var buffer = ArrayPool<byte>.Shared.Rent(segment.Count);
            segment.AsSpan().CopyTo(buffer);

            _fragments[index] = buffer;
            _lengths[index] = segment.Count;
            _totalSize += segment.Count;
            return ++ReceivedCount == ExpectedCount;
        }

        public (byte[] buffer, int length) Combine()
        {
            var combined = ArrayPool<byte>.Shared.Rent(_totalSize);
            for (int i = 0, offset = 0; i < ExpectedCount; i++)
            {
                Buffer.BlockCopy(_fragments[i], 0, combined, offset, _lengths[i]);
                offset += _lengths[i];
            }

            return (combined, _totalSize);
        }

        public void Dispose()
        {
            for (var i = 0; i < ExpectedCount; i++)
            {
                if (_fragments[i] == null) continue;
                ArrayPool<byte>.Shared.Return(_fragments[i]);
            }

            ArrayPool<byte[]>.Shared.Return(_fragments);
            ArrayPool<int>.Shared.Return(_lengths);
        }
    }
    
    public sealed class MessageAssembler : IDisposable
    {
        private readonly ConcurrentDictionary<uint, FragmentReassembly> _states =
            new ConcurrentDictionary<uint, FragmentReassembly>();

        public bool TryAssemble(FragmentHeader header, ArraySegment<byte> segment, out byte[] buffer, out int length)
        {
            buffer = null;
            length = 0;

            var state = _states.GetOrAdd(header.FragId, _ => new FragmentReassembly(header.TotalCount));

            lock (state)
            {
                if (!state.Add(header.Index, segment))
                {
                    if (state.ReceivedCount == 0) Discard(header.FragId);
                    return false;
                }

                if (state.ReceivedCount < state.ExpectedCount) return false;

                _states.TryRemove(header.FragId, out _);

                var result = state.Combine();
                buffer = result.buffer;
                length = result.length;
                
                state.Dispose();
                return true;
            }
        }

        private void Discard(uint id)
        {
            if (_states.TryGetValue(id, out var s))
                s.Dispose();
        }

        public void Cleanup(TimeSpan timeout)
        {
            var limit = DateTime.UtcNow.Ticks - timeout.Ticks;
            foreach (var (key, state) in _states)
            {
                if (state.CreateAt < limit)
                    Discard(key);
            }
        }

        public void Dispose()
        {
            foreach (var key in _states.Keys)
                Discard(key);
        }
    }
}
    
