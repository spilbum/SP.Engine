using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using SP.Core;

namespace SP.Engine.Runtime.Networking
{
    public sealed class SessionSendBuffer : IDisposable
    {
        private readonly PooledBuffer _buffer;
        private readonly int _capacity;
        private int _head;
        private int _tail;
        private readonly object _lock = new object();
        private bool _wrapped;
        private bool _disposed;
        
        public int Capacity => _capacity;
        public int DataSize => GetDataSize();
        
        public SessionSendBuffer(int initialCapacity)
        {
            _buffer = new PooledBuffer(initialCapacity);
            _capacity = _buffer.Capacity;
        }

        /// <summary>
        /// 송신을 위한 메모리 공간을 예약합니다.
        /// </summary>
        public bool TryReserve(int size, out ArraySegment<byte> segment, out Span<byte> span)
        {
            segment = default;
            span = default;
            
            if (_disposed || size <= 0 || size > _capacity) return false;

            lock (_lock)
            {
                if (!_wrapped)
                {
                    // 선형 상태: [....H------T....]
                    // 남은 공간: 끝부분(Length - _tail) + 앞부분(_head)
                    if (_tail + size <= _capacity)
                    {
                        // 끝부분에 바로 할당 가능
                        span = _buffer.Slice(_tail, size);
                        segment = _buffer.AsSegment(_tail, size);
                        _tail += size;
                        return true;
                    }

                    // 앞부분으로 회전(Wrap) 가능한지 확인
                    if (_head > size)
                    {
                        _wrapped = true;
                        _tail = size;
                        span = _buffer.Slice(0, size);
                        segment = _buffer.AsSegment(0, size);
                        return true;
                    }
                }
                else
                {
                    // 순환 상태: [----T.....H----]
                    // 중간 여유 공간 확인
                    var freeSpace = _head - _tail;
                    if (freeSpace > size)
                    {
                        span = _buffer.Slice(_tail, size);
                        segment = _buffer.AsSegment(_tail, size);
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
        public void Release(int transferred)
        {
            if (_disposed || transferred <= 0) return;

            lock (_lock)
            {
                if (!_wrapped)
                {
                    // 선형 상태에서는 단순히 head 이동
                    _head = Math.Max(_head + transferred, _tail);
                }
                else
                {
                    var distanceToEnd = _capacity - _head;
                    if (transferred < distanceToEnd)
                    {
                        // 아직 버퍼 끝에 도달하지 않음
                        _head += transferred;
                    }
                    else
                    {
                        // 버퍼 끝을 도달하여 다시 앞부분으로 회전
                        _head = transferred - distanceToEnd;
                        _wrapped = false;
                        
                        // 논리적 오류 방지: head가 tail을 넘지 않도록 보정
                        if (_head > _tail) _head = _tail;
                    }
                }
                
                // 완전히 비었을 때 포인터 초기화
                if (_head == _tail && !_wrapped)
                {
                    _head = 0;
                    _tail = 0;
                }
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int GetDataSize()
        {
            lock (_lock)
            {
                return _wrapped
                    ? _capacity - _head + _tail
                    : _tail - _head;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _buffer.Dispose();
        }
    }
}
