using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO.Enumeration;
using System.Linq;
using SP.Core;

namespace SP.Engine.Runtime.Networking
{
    internal sealed class ReassemblyState
    {
        public readonly byte[][] Parts;
        public readonly DateTime CreatedAtUtc = DateTime.UtcNow;
        public readonly byte TotalCount;
        public int ReceivedCount;
        public int TotalLength;

        public ReassemblyState(byte totalCount)
        {
            TotalCount = totalCount;
            Parts = new byte[totalCount][];
        }
    }

    public interface IFragmentAssembler
    {
        bool TryAssemble(FragmentHeader fragHeader, ArraySegment<byte> fragSegment, out ArraySegment<byte> assembled);
        void Cleanup(TimeSpan timeout);
    }

    public sealed class FragmentAssembler : IFragmentAssembler
    {
        private readonly ConcurrentDictionary<uint, ReassemblyState> _states =
            new ConcurrentDictionary<uint, ReassemblyState>();

        public bool TryAssemble(FragmentHeader fragHeader, ArraySegment<byte> fragSegment, out ArraySegment<byte> assembled)
        {
            assembled = default;

            if (fragHeader.Index >= fragHeader.TotalCount)
            {
                return false;
            }

            var key = fragHeader.FragId;
            
            if (!_states.TryGetValue(key, out var state))
            {
                state = new ReassemblyState(fragHeader.TotalCount);
                if (!_states.TryAdd(key, state))
                {
                    if (!_states.TryGetValue(key, out state))
                    {
                        return false;
                    }
                }
            }

            lock (state)
            {
                const int MaxReassemblySize = 1024 * 1024;
                if (state.TotalLength + fragSegment.Count > MaxReassemblySize)
                {
                    _states.TryRemove(key, out _);
                    return false;
                }
                 
                state.Parts[fragHeader.Index] = fragSegment.ToArray();
                state.ReceivedCount++;
                state.TotalLength += fragSegment.Count;

                if (state.ReceivedCount != state.TotalCount)
                    return false;

                var resultData = new byte[state.TotalLength];
                var offset = 0;

                for (var i = 0; i < state.TotalCount; i++)
                {
                    var part = state.Parts[i];
                    if (part == null)
                    {
                        _states.TryRemove(key, out _);
                        return false;
                    }
                    
                    Buffer.BlockCopy(part, 0, resultData, offset, part.Length);
                    offset += part.Length;
                }

                _states.TryRemove(key, out _);
                
                assembled = new ArraySegment<byte>(resultData);
                return true;
            }
        }

        public void Cleanup(TimeSpan timeout)
        {
            var now = DateTime.UtcNow;

            foreach (var (key, state) in _states)
            {
                if (now - state.CreatedAtUtc < timeout) continue;
                _states.TryRemove(key, out _);
            }
        }
    }
}
