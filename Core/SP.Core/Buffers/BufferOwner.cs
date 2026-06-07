using System;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;

namespace SP.Core.Buffers
{
    public sealed class BufferOwner : IMemoryOwner<byte>
    {
        private byte[] _buffer;
        private int _disposed;
        private int _capacity;
        
        #if DEBUG
        private long _bufferId;
        private StackTrace _stackTrace;

        private static long _globalBufferId;
        private static readonly ConcurrentDictionary<long, (StackTrace Trace, DateTime Time)> _activeRegistry 
             = new ConcurrentDictionary<long, (StackTrace, DateTime)>();
        #endif

        public Memory<byte> Memory
        {
            get
            {
                ThrowIfDisposed();
                return _buffer.AsMemory(0, _capacity);
            }
        }
        
        public int Length => _buffer.Length;
        
        public BufferOwner(int capacity)
        {
            Initialize(capacity);
        }

        internal void Initialize(int capacity)
        {
            _capacity = capacity;
            _buffer = ArrayPool<byte>.Shared.Rent(capacity);
            _disposed = 0;
            BufferMetrics.OnRent();
            
#if DEBUG
            _stackTrace = new StackTrace(1, true);
            _bufferId = Interlocked.Increment(ref _globalBufferId);
            _activeRegistry.TryAdd(_bufferId, (_stackTrace, DateTime.UtcNow));
#endif
        }
        
        #if DEBUG
        ~BufferOwner()
        {
            if (_disposed == 0 && _buffer != null)
            {
                ReportLeak();
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private void ReportLeak()
        {
            var sb = new StringBuilder();
            sb.AppendLine("\n[CRITICAL MEMORY LEAK DETECTED]");
            sb.AppendLine($"A {nameof(BufferOwner)} was garbage collected without being properly disposed.");
            sb.AppendLine("Allocation Stack Trace:");
            sb.AppendLine(_stackTrace?.ToString() ?? "No trace available");

            var alertMessage = sb.ToString();
            
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(alertMessage);
            Console.ResetColor();
            
            Debug.Fail(alertMessage);
        }

        public static void DumpActiveBuffers(Action<string> logWriter = null)
        {
            if (_activeRegistry.IsEmpty)
            {
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("\n[MEMORY CHECK] All PooledBuffers are cleanly disposed. Zero leaks.");
                Console.ResetColor();
                return;
            }

            var sb = new StringBuilder();
            sb.AppendLine($"\n[CRITICAL MEMORY LEAK DETECTED - SERVER SHUTDOWN]");
            sb.AppendLine($"Total unreleased buffers remaining in memory: {_activeRegistry.Count}\n");

            var index = 1;
            foreach (var kvp in _activeRegistry)
            {
                sb.AppendLine($"--- Leak Node #{index++} (Buffer ID: {kvp.Key} | AllocTime: {kvp.Value.Time}) ---");
                // 덤프 요청 시점에만 문자열로 변환
                sb.AppendLine(kvp.Value.Trace?.ToString()); 
                sb.AppendLine();
            }

            var report = sb.ToString();

            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine(report);
            Console.ResetColor();

            logWriter?.Invoke(report);
            Debug.Fail($"Memory Leak Detected! Remaining buffers: {_activeRegistry.Count}");
        }
#endif
        
        public byte[] GetBuffer() => _buffer;
        
        public Span<byte> Slice(int start, int length)
        {
            ThrowIfDisposed();
            return _buffer.AsSpan(start, length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void ThrowIfDisposed()
        {
            if (_disposed != 0) ThrowObjectDisposedException();
        }
        
        [method: MethodImpl(MethodImplOptions.NoInlining)]
        private static void ThrowObjectDisposedException() => throw new ObjectDisposedException(nameof(BufferOwner));

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 1) return;
            
            #if DEBUG
            GC.SuppressFinalize(this);
            _activeRegistry.TryRemove(_bufferId, out _);
            #endif
            
            var buf = _buffer;
            _buffer = null;

            if (buf != null)
            {
                ArrayPool<byte>.Shared.Return(buf);
                BufferMetrics.OnReturn();   
            }

            BufferOwnerPool.Return(this);
        }
    }
}
