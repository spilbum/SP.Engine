using System;
using System.Collections.Concurrent;
using System.Linq;

namespace SP.Engine.Runtime.Message
{
    internal class FragmentBuffer
    {
        private readonly ArraySegment<byte>[] _fragments;
        private readonly bool[] _received;
        private readonly int _totalCount;
        private int _receivedCount;

        public FragmentBuffer(byte totalCount)
        {
            _fragments = new ArraySegment<byte>[totalCount];
            _received = new bool[totalCount];
            _totalCount = totalCount;
        }

        public bool Add(byte index, ArraySegment<byte> segment)
        {
            if (index >= _totalCount)
                return false;
            if (_received[index])
                return false;
            
            _fragments[index] = segment;
            _received[index] = true;
            _receivedCount++;
            return true;
        }

        public bool IsComplete => _receivedCount == _totalCount;

        public ArraySegment<byte> Assemble()
        {
            var length = UdpHeader.HeaderSize + _fragments.Sum(f => f.Count);
            var payload = new byte[length];
            var offset = 0;
            foreach (var fragment in _fragments)
            {
                fragment.AsSpan().CopyTo(payload.AsSpan(offset));
                offset += fragment.Count;
            }
            return new ArraySegment<byte>(payload, 0, length);
        }
    }
    
    public class UdpFragmentAssembler
    {
        private readonly ConcurrentDictionary<long, (FragmentBuffer Buffer, DateTime ExpireTime)> _bufferDict =
            new ConcurrentDictionary<long, (FragmentBuffer, DateTime)>();
        
        private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

        public bool TryAssemble(long sequenceNumber, UdpFragment fragment, out ArraySegment<byte> payload)
        {
            payload = default;
            var tuple = _bufferDict.GetOrAdd(sequenceNumber, _ =>
            {
                var buffer = new FragmentBuffer(fragment.TotalCount);
                var expireTime = DateTime.UtcNow.Add(DefaultTimeout);
                return (buffer, expireTime);
            });

            if (!tuple.Buffer.Add(fragment.Index, fragment.Payload))
                return false;
            
            if (!tuple.Buffer.IsComplete) 
                return false;
            
            payload = tuple.Buffer.Assemble();
            _bufferDict.TryRemove(sequenceNumber, out _);
            return true;
        }
    }
}
