using System;
using System.Buffers;
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
        public readonly byte TotalCount;
        public readonly ArraySegment<byte>[] Parts;
        public int ReceivedCount;
        public int TotalLength;

        public ReassemblyState(byte totalCount)
        {
            TotalCount = totalCount;
            Parts = new ArraySegment<byte>[totalCount];
        }
    }

    public interface IUdpFragmentAssembler
    {
        /// <summary>
        /// 조각을 추가하고, 모두 모이면 contiguous payload를 만들어 반환.
        /// 반환된 payload는 ArrayPool 버퍼를 참조할 수 있으므로, 사용 후 ReturnPayload 호출 필요.
        /// </summary>
        bool TryAssemble(
            UdpHeader header, 
            UdpFragmentHeader fragHeader,
            ArraySegment<byte> fragPayload,
            out ArraySegment<byte> assembledPayload);

        int SweepExpired(TimeSpan timeout);
        void ReturnPayload(ArraySegment<byte> payload);
    }

    public sealed class UdpFragmentAssembler : IUdpFragmentAssembler
    {
        private readonly ConcurrentDictionary<FragmentKey, ReassemblyState> _map =
            new ConcurrentDictionary<FragmentKey, ReassemblyState>();

        private readonly ArrayPool<byte> _pool;
        private readonly int _maxTotalBytes;

        public UdpFragmentAssembler(ArrayPool<byte> pool = null, int maxTotalBytesPerMessage = 4 * 1024 * 1024)
        {
            _pool = pool ?? ArrayPool<byte>.Shared;
            _maxTotalBytes = maxTotalBytesPerMessage;
        }

        public bool TryAssemble(UdpHeader header, UdpFragmentHeader fragHeader,
            ArraySegment<byte> fragmentPayload,
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
                    state.Parts[fragHeader.Index] = fragmentPayload; // 제로-카피 보관
                    state.ReceivedCount++;
                    state.TotalLength += fragmentPayload.Count;

                    if (state.TotalLength > _maxTotalBytes)
                    {
                        _map.TryRemove(key, out _);
                        return false;
                    }
                }

                if (state.ReceivedCount != state.TotalCount)
                    return false;

                // 모두 모임 → 1회만 합치기
                var dst = _pool.Rent(state.TotalLength);
                var write = 0;
                for (int i = 0; i < state.TotalCount; i++)
                {
                    var seg = state.Parts[i];
                    if (seg.Array == null)
                    {
                        _map.TryRemove(key, out _);
                        return false;
                    }

                    Buffer.BlockCopy(seg.Array!, seg.Offset, dst, write, seg.Count);
                    write += seg.Count;
                }

                _map.TryRemove(key, out _);
                assembledPayload = new ArraySegment<byte>(dst, 0, state.TotalLength);
                return true;
            }
        }

        public int SweepExpired(TimeSpan timeout)
        {
            var now = DateTime.UtcNow;
            var removed = 0;
            foreach (var kv in _map)
            {
                if (now - kv.Value.CreatedAtUtc >= timeout && _map.TryRemove(kv.Key, out _))
                    removed++;
            }

            return removed;
        }

        public void ReturnPayload(ArraySegment<byte> payload)
        {
            if (payload.Array != null)
                _pool.Return(payload.Array);
        }
    }
}
