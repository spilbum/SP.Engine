using System;
using System.Collections.Concurrent;
using System.IO.Enumeration;
using System.Linq;
using SP.Core;

namespace SP.Engine.Runtime.Networking
{
    internal sealed class ReassemblyState : IDisposable
    {
        public readonly PooledBuffer[] Parts;
        public readonly DateTime CreatedAtUtc = DateTime.UtcNow;
        public readonly byte TotalCount;
        public int ReceivedCount;
        public int TotalLength;

        public ReassemblyState(byte totalCount)
        {
            TotalCount = totalCount;
            Parts = new PooledBuffer[totalCount];
        }

        public void Dispose()
        {
            for (var i = 0; i < Parts.Length; i++)
            {
                Parts[i].Dispose();
            }
        }
    }

    public interface IFragmentAssembler
    {
        bool TryAssemble(
            UdpHeader header,
            FragmentHeader fragHeader,
            PooledBuffer fragPayload,
            out PooledBuffer assembled);

        void Cleanup(TimeSpan timeout);
    }

    public sealed class FragmentAssembler : IFragmentAssembler
    {
        private readonly ConcurrentDictionary<uint, ReassemblyState> _map =
            new ConcurrentDictionary<uint, ReassemblyState>();

        public bool TryAssemble(
            UdpHeader header,
            FragmentHeader fragHeader,
            PooledBuffer fragPayload,
            out PooledBuffer assembled)
        {
            assembled = default;

            if (fragHeader.Index >= fragHeader.TotalCount)
            {
                fragPayload.Dispose();
                return false;
            }

            var key = fragHeader.FragId;
            
            if (!_map.TryGetValue(key, out var state))
            {
                state = new ReassemblyState(fragHeader.TotalCount);
                if (!_map.TryAdd(key, state))
                {
                    if (!_map.TryGetValue(key, out state))
                    {
                        fragPayload.Dispose();
                        return false;
                    }
                }
            }

            lock (state)
            {
                const int MaxReassemblySize = 1024 * 1024;
                if (state.TotalLength + fragPayload.Count > MaxReassemblySize)
                {
                    fragPayload.Dispose();
                    _map.TryRemove(key, out _);
                    state.Dispose();
                    return false;
                }
                 
                if (state.Parts[fragHeader.Index].Array != null)
                {
                    fragPayload.Dispose();
                    return false;
                }
                
                state.Parts[fragHeader.Index] = fragPayload;
                state.ReceivedCount++;
                state.TotalLength += fragPayload.Count;

                if (state.ReceivedCount != state.TotalCount)
                    return false;

                var result = new PooledBuffer(state.TotalLength);
                var offset = 0;

                try
                {
                    for (var i = 0; i < state.TotalCount; i++)
                    {
                        var part = state.Parts[i];
                        part.Span.CopyTo(result.Span[offset..]);
                        offset += part.Count;
                    }
                }
                catch
                {
                    result.Dispose();
                    throw;
                }

                _map.TryRemove(key, out _);
                state.Dispose();
                
                assembled = result;
                return true;
            }
        }

        public void Cleanup(TimeSpan timeout)
        {
            var now = DateTime.UtcNow;

            foreach (var (key, state) in _map.ToArray())
            {
                if (now - state.CreatedAtUtc < timeout) continue;
                if (!_map.TryRemove(key, out var removedState)) continue;

                lock (removedState)
                {
                    removedState.Dispose();
                }
            }
        }
    }
}
