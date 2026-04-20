using System;
using System.Buffers;
using System.Collections.Concurrent;
using SP.Core;

namespace SP.Engine.Runtime.Networking
{
    internal sealed class FragmentReassembly : IDisposable
    {
        private readonly RentedBuffer[] _fragments;
        private int _totalLength;
        
        public readonly long CreateAt = DateTime.UtcNow.Ticks;
        public int ReceivedCount { get; private set; }
        public byte ExpectedCount { get; }

        public FragmentReassembly(byte count)
        {
            ExpectedCount = count;
            _fragments = ArrayPool<RentedBuffer>.Shared.Rent(count);
            Array.Clear(_fragments, 0, count);
        }

        public bool Add(byte index, ArraySegment<byte> segment)
        {
            if (index >= ExpectedCount || _fragments[index].Span.Length > 0) return false;

            var buffer = new RentedBuffer(segment.Count);
            segment.AsSpan().CopyTo(buffer.Span);

            _fragments[index] = buffer;
            _totalLength += segment.Count;
            ReceivedCount++;
            return true;
        }

        public RentedBuffer Combine()
        {
            var result = new RentedBuffer(_totalLength);
            var dest = result.Span;
            var offset = 0;
            
            for (var i = 0; i < ExpectedCount; i++)
            {
                var src = _fragments[i].Span;
                src.CopyTo(dest[offset..]);
                offset += src.Length;
            }
    
            return result;
        }

        public void Dispose()
        {
            for (var i = 0; i < ExpectedCount; i++)
            {
                _fragments[i].Dispose();
            }

            ArrayPool<RentedBuffer>.Shared.Return(_fragments);
        }
    }
    
    public sealed class MessageAssembler : IDisposable
    {
        private readonly ConcurrentDictionary<uint, FragmentReassembly> _states =
            new ConcurrentDictionary<uint, FragmentReassembly>();

        public bool TryAssemble(FragmentHeader header, ArraySegment<byte> segment, out RentedBuffer buffer)
        {
            buffer = default;

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

                using (state)
                {
                    buffer = state.Combine();
                }
                
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
    
