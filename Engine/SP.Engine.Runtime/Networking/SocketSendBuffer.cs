using System;
using SP.Core;

namespace SP.Engine.Runtime.Networking
{
    public sealed class SocketSendBuffer : IDisposable
    {
        private RentedBuffer _buffer;
        private int _head;
        private int _tail;
        private readonly object _lock = new object();
        private bool _wrapped;
        private bool _disposed;
        
        public SocketSendBuffer(int capacity)
        {
            _buffer = new RentedBuffer(capacity);
        }

        /// <summary>
        /// 송신을 위한 메모리 공간을 예약합니다.
        /// 변환된 Span에 데이터를 기록한 뒤, Segment를 전송 큐에 담으십시오.
        /// </summary>
        /// <param name="size"></param>
        /// <param name="segment"></param>
        /// <param name="span"></param>
        /// <returns></returns>
        public bool TryReserve(int size, out ArraySegment<byte> segment, out Span<byte> span)
        {
            segment = default;
            span = default;
            
            if (_disposed || size <= 0 || size > _buffer.Length) return false;

            lock (_lock)
            {
                if (!_wrapped)
                {
                    // 선형 상태: [....H------T....]
                    // 남은 공간: 끝부분(Length - _tail) + 앞부분(_head)
                    if (_tail + size <= _buffer.Length)
                    {
                        // 끝부분에 바로 할당 가능
                        span = _buffer.Span.Slice(_tail, size);
                        segment = CreateSegment(_tail, size);
                        _tail += size;
                        return true;
                    }

                    if (_head > size)
                    {
                        // 앞부분으로 회전 가능
                        _tail = size;
                        _wrapped = true;
                        span = _buffer.Span.Slice(0, size);
                        segment = CreateSegment(0, size);
                        return true;
                    }
                }
                else
                {
                    // 순환 상태: [----T.....H----]
                    var freeSpace = _head - _tail;
                    if (freeSpace > size)
                    {
                        span = _buffer.Span.Slice(_tail, size);
                        segment = CreateSegment(_tail, size);
                        _tail += size;
                        return true;
                    }
                }

                return false;
            }
        }

        /// <summary>
        /// 소켓 전송이 완료된 바이트만큼 공간을 해제합니다
        /// </summary>
        /// <param name="transferred"></param>
        public void Release(int transferred)
        {
            if (_disposed || transferred <= 0) return;

            lock (_lock)
            {
                var distanceToEnd = _buffer.Length - _head;
                if (_wrapped && transferred >= distanceToEnd)
                {
                    _head = transferred - distanceToEnd;
                    _wrapped = false; // 다시 선형 상태로
                }
                else
                {
                    _head += transferred;
                }
                
                // 완전히 비었을 때 위치 초기화
                if (_head == _tail && !_wrapped)
                {
                    _head = 0;
                    _tail = 0;
                }
            }
        }
        
        private ArraySegment<byte> CreateSegment(int offset, int size)
            => _buffer.AsSegment(offset, size);

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _buffer.Dispose();
        }
    }
}
