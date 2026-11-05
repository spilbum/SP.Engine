using System.Diagnostics;
using Microsoft.Extensions.Logging;
using SP.Core.Logging;

namespace SP.Core
{
    public readonly ref struct SlowChecker
    {
        private static readonly ILogger DefaultLogger = LogManager.GetLogger(nameof(SlowChecker));

        private readonly long _startTicks;
        private readonly int _thresholdMs;
        private readonly string _title;
        private readonly ILogger _logger;

        public SlowChecker(int thresholdMs, string title, ILogger logger = null)
        {
            _startTicks = Stopwatch.GetTimestamp();
            _thresholdMs = thresholdMs;
            _title = title;
            _logger = logger ?? DefaultLogger;
        }

        public void Dispose()
        {
            var elapsedMs = (Stopwatch.GetTimestamp() - _startTicks) * 1000.0 / Stopwatch.Frequency;
            if (elapsedMs > _thresholdMs)
            {
                _logger?.Warn("[{0}] Slow: {1:F2}ms", _title, elapsedMs);
            }
        }
    }
}
