using System;
using System.Buffers;
using System.IO;
using System.Runtime.CompilerServices;
using SP.Core.Buffers;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client
{
    public sealed class ByteReadWriteBuffer
    {
        private readonly byte[] _buffer;
        private readonly int _capacity;
        private readonly int _mask;
        
        private int _head;
        private int _tail;
        private int _size;
        
        private readonly object _lock = new object();
        
        public ByteReadWriteBuffer(int capacity)
        {
            var cap = 1;
            while (cap < capacity) cap <<= 1;
            
            _capacity = cap;
            _mask = cap - 1;
            _buffer = new byte[cap];
        }
        
        public bool TryWrite(ReadOnlySpan<byte> data)
        {
            lock (_lock)
            {
                if (data.IsEmpty) return false;
                if (data.Length > _capacity - _size) return false;
                
                if (_tail + data.Length <= _capacity)
                {
                    data.CopyTo(_buffer.AsSpan(_tail, data.Length));
                    _tail = (_tail + data.Length) & _mask;
                }
                else
                {
                    var firstChunk = _capacity - _tail;
                    var secondChunk = data.Length - firstChunk;
                    
                    data.Slice(0, firstChunk).CopyTo(_buffer.AsSpan(_tail, firstChunk));
                    data.Slice(firstChunk, secondChunk).CopyTo(_buffer.AsSpan(0, secondChunk));

                    _tail = secondChunk;
                }
                
                _size += data.Length;
                return true;
            }
        }
        
         public bool TryRead(
            IPolicySnapshot policySnapshot, 
            out TcpHeader header,
            out IMemoryOwner<byte> bufferOwner)
        {
            header = default;
            bufferOwner = null;
            const int headerSize = TcpHeader.ByteSize;

            lock (_lock)
            {
                // 최소 헤더 크기만큼 데이터가 있는지 확인
                if (_size < headerSize) return false;
                
                Span<byte> headerSpan = stackalloc byte[headerSize];
                CopyTo(headerSize, headerSpan);

                if (!TcpHeader.TryRead(headerSpan, out var tempHeader, out var headerConsumed)) return false;
                
                // 전체 패킷이 도착했는지 확인
                var payloadLen = tempHeader.PayloadLength;
                var totalLength = headerConsumed + payloadLen;
                
                if (_size < totalLength) return false;

                // 패킷 별 최대 페이로드 용량 체크
                var maxPayloadLength = policySnapshot.Resolve(header.ProtocolId)?.MaxPayloadLength ?? 65536;
                if (payloadLen < 0 || payloadLen > maxPayloadLength)
                {
                    throw new InvalidDataException($"Corrupted payload detected. ID: {tempHeader.ProtocolId}, BodyLen: {payloadLen}, Max: {maxPayloadLength}");
                }
                
                header = tempHeader;

                var buffer = BufferOwnerPool.Rent(totalLength);
                CopyTo(totalLength, buffer.Memory.Span);
                
                bufferOwner = buffer;
                
                // 상태 갱신
                _head = (_head + totalLength) & _mask;
                _size -= totalLength;
                
                // 버퍼가 완전히 비었을 경우 포인터 초기화로 파편화 방지
                if (_size == 0)
                {
                    _head = _tail = 0;
                }

                return true;
            }
        }

        /// <summary>
        /// 링 버퍼의 데이터를 연속된 Span으로 복사합니다.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void CopyTo(int length, Span<byte> destination)
        {
            var distanceToEnd = _capacity - _head;
            if (distanceToEnd >= length)
            {
                _buffer.AsSpan(_head, length).CopyTo(destination);
            }
            else
            {
                _buffer.AsSpan(_head, distanceToEnd).CopyTo(destination.Slice(0, distanceToEnd));
                _buffer.AsSpan(0, length - distanceToEnd).CopyTo(destination.Slice(distanceToEnd, length - distanceToEnd));
            }
        }
    }
}
