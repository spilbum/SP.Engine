using System;
using System.Collections.Concurrent;
using System.Linq;

namespace SP.Engine.Runtime.Networking
{
    public readonly struct FragmentKey : IEquatable<FragmentKey>
    {
        private readonly uint _peerId;
        private readonly ushort _msgId;
        private readonly uint _fragId;

        public FragmentKey(uint peerId, ushort msgId, uint fragId)
        {
            _peerId = peerId;
            _msgId = msgId;
            _fragId = fragId;
        }

        public bool Equals(FragmentKey other) =>
            _peerId == other._peerId && _msgId == other._msgId && _fragId == other._fragId;

        public override bool Equals(object obj) => obj is FragmentKey k && Equals(k);
        public override int GetHashCode() => HashCode.Combine(_peerId, _msgId, _fragId);
    }

    internal sealed class ReassemblyState
    {
        public readonly DateTime CreatedAtUtc = DateTime.UtcNow;
        public readonly ushort TotalCount;
        public readonly ArraySegment<byte>[] Parts;
        public int ReceivedCount;
        public int TotalLength;

        public ReassemblyState(ushort totalCount)
        {
            TotalCount = totalCount;
            Parts = new ArraySegment<byte>[totalCount];
        }
    }

    public interface IUdpFragmentAssembler
    {
        bool TryAssemble(
            UdpHeader header, 
            UdpFragmentHeader fragHeader,
            ArraySegment<byte> fragPayload,
            out ArraySegment<byte> assembledPayload);

        int Cleanup(TimeSpan timeout);
    }

    public sealed class UdpFragmentAssembler : IUdpFragmentAssembler
    {
        private readonly ConcurrentDictionary<FragmentKey, ReassemblyState> _map =
            new ConcurrentDictionary<FragmentKey, ReassemblyState>();

        public bool TryAssemble(
            UdpHeader header, 
            UdpFragmentHeader fragHeader,
            ArraySegment<byte> fragPayload,
            out ArraySegment<byte> assembledPayload)
        {
            assembledPayload = default;

            var key = new FragmentKey(header.PeerId, header.Id, fragHeader.Id);
            var state = _map.GetOrAdd(key, _ => new ReassemblyState(fragHeader.TotalCount));

            if (fragHeader.Index >= state.TotalCount || fragHeader.TotalCount != state.TotalCount)
                return false;

            lock (state)
            {
                if (state.Parts[fragHeader.Index].Array == null)
                {
                    state.Parts[fragHeader.Index] = fragPayload; 
                    state.ReceivedCount++;
                    state.TotalLength += fragPayload.Count;
                }

                if (state.ReceivedCount != state.TotalCount)
                    return false;

                var dst =  new byte[state.TotalLength];
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
                }

                _map.TryRemove(key, out _);
                assembledPayload = new ArraySegment<byte>(dst, 0, state.TotalLength);
                return true;
            }
        }

        public int Cleanup(TimeSpan timeout)
        {
            var now = DateTime.UtcNow;
            return _map.Count(kv => now - kv.Value.CreatedAtUtc >= timeout && _map.TryRemove(kv.Key, out _));
        }
    }
}
