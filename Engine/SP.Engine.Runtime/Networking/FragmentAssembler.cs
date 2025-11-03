using System;
using System.Collections.Concurrent;

namespace SP.Engine.Runtime.Networking
{
    internal sealed class ReassemblyState
    {
        public readonly DateTime CreatedAtUtc = DateTime.UtcNow;
        public readonly ArraySegment<byte>[] Parts;
        public readonly byte TotalCount;
        public int ReceivedCount;
        public int TotalLength;

        public ReassemblyState(byte totalCount)
        {
            TotalCount = totalCount;
            Parts = new ArraySegment<byte>[totalCount];
        }
    }

    public interface IFragmentAssembler
    {
        bool TryAssemble(
            UdpHeader header,
            FragmentHeader fragHeader,
            ArraySegment<byte> fragPayload,
            out ArraySegment<byte> assembled);

        void Cleanup(TimeSpan timeout);
    }

    public sealed class FragmentAssembler : IFragmentAssembler
    {
        private readonly ConcurrentDictionary<uint, ReassemblyState> _map =
            new ConcurrentDictionary<uint, ReassemblyState>();

        public bool TryAssemble(
            UdpHeader header,
            FragmentHeader fragHeader,
            ArraySegment<byte> fragPayload,
            out ArraySegment<byte> assembled)
        {
            assembled = default;

            if (fragPayload.Array == null || fragPayload.Count < 0) return false;
            if (fragHeader.Index >= fragHeader.TotalCount) return false;

            var key = fragHeader.FragId;
            var state = _map.GetOrAdd(key, _ => new ReassemblyState(fragHeader.TotalCount));

            if (state.TotalCount != fragHeader.TotalCount) return false;

            lock (state)
            {
                var slot = state.Parts[fragHeader.Index];
                if (slot.Array == null)
                {
                    state.Parts[fragHeader.Index] = fragPayload;
                    state.TotalLength += fragPayload.Count;
                    state.ReceivedCount++;
                }

                if (state.ReceivedCount != state.TotalCount)
                    return false;

                // 조립
                var dst = new byte[state.TotalLength];
                var offset = 0;
                for (var i = 0; i < state.TotalCount; i++)
                {
                    var seg = state.Parts[i];
                    if (seg.Array == null)
                    {
                        _map.TryRemove(key, out _);
                        return false;
                    }

                    Buffer.BlockCopy(seg.Array!, seg.Offset, dst, offset, seg.Count);
                    offset += seg.Count;
                    state.Parts[i] = default;
                }

                _map.TryRemove(key, out _);
                assembled = new ArraySegment<byte>(dst);
                return true;
            }
        }

        public void Cleanup(TimeSpan timeout)
        {
            var now = DateTime.UtcNow;

            foreach (var (key, state) in _map.ToArray())
            {
                if (now - state.CreatedAtUtc < timeout) continue;
                if (!_map.TryRemove(key, out var s)) continue;

                lock (s)
                {
                    for (var i = 0; i < s.Parts.Length; i++)
                        s.Parts[i] = default;
                }
            }
        }
    }
}
