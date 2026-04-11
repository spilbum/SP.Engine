using System.Collections.Concurrent;

namespace GameClient;

public sealed class UdpQualityTracker
{
    private struct UdpSample
    {
        public uint Seq;
        public long SentTicks;
        public long ReceivedTicks;
        public double RttMs => ReceivedTicks > 0 ? (ReceivedTicks - SentTicks) / (double)TimeSpan.TicksPerMillisecond : 0;
    }

    private readonly ConcurrentDictionary<uint, UdpSample> _samples = new();
    private readonly object _jitterLock = new();
    
    // 통계 카운터
    private long _totalSent;
    private long _totalReceived;
    private long _outOfOrderCount;
    private uint _nextSeq = 1;
    private uint _lastReceivedSeq;

    // 지터 계산용
    private double _lastRtt = -1;
    private double _cumulativeJitter;
    private long _jitterSampleCount;
    
    private const long LossTimeoutTicks = TimeSpan.TicksPerSecond * 2; // 2초간 무응답 시 유실 확정
    private const int MaxSampleRetention = 5000; // 메모리 보호를 위한 최대 샘플 보유량
    
    /// <summary>
    /// 송신 시 호출하여 시퀀스 번호를 발급받고 기록합니다.
    /// </summary>
    public uint RecordSend()
    {
        var seq = Interlocked.Increment(ref _nextSeq) - 1;
        var now = DateTime.UtcNow.Ticks;

        var sample = new UdpSample { Seq = seq, SentTicks = now };
        _samples.TryAdd(seq, sample);
        Interlocked.Increment(ref _totalSent);

        if (seq % 1000 == 0) CleanupOldSamples(now);

        return seq;
    }

    public void RecordReceive(uint seq)
    {
        if (!_samples.TryRemove(seq, out var sample)) return;
        
        var now = DateTime.UtcNow.Ticks;
        sample.ReceivedTicks = now;
        Interlocked.Increment(ref _totalReceived);

        if (seq < _lastReceivedSeq) Interlocked.Increment(ref _outOfOrderCount);
        _lastReceivedSeq = Math.Max(_lastReceivedSeq, seq);

        lock (_jitterLock)
        {
            UpdateJitter(sample.RttMs);
        }
    }

    private void UpdateJitter(double currentRtt)
    {
        if (_lastRtt >= 0)
        {
            var diff = Math.Abs(currentRtt - _lastRtt);
            _cumulativeJitter += diff;
            Interlocked.Increment(ref _jitterSampleCount);
        }
        _lastRtt = currentRtt;
    }

    private void CleanupOldSamples(long now)
    {
        foreach (var key in _samples.Keys.ToList())
        {
            if (!_samples.TryGetValue(key, out var sample)) continue;
            if (now - sample.SentTicks <= LossTimeoutTicks) continue;
            _samples.TryRemove(key, out _);
        }
    }

    public QualityReport GetReport()
    {
        var now = DateTime.UtcNow.Ticks;
        var sent = Interlocked.Read(ref _totalSent);
        var received = Interlocked.Read(ref _totalReceived);
        
        var inFlight = _samples.Values.Count(s => now - s.SentTicks < LossTimeoutTicks);
        
        var definiteLoss = sent - received - inFlight;
        var lossRate = sent > 0 ? (float)Math.Max(0, definiteLoss) / sent * 100f : 0;

        return new QualityReport
        {
            TotalSent = sent,
            TotalReceived = received,
            LossRate = lossRate,
            OutOfOrderCount = Interlocked.Read(ref _outOfOrderCount),
            AvgJitterMs = _jitterSampleCount > 0 ? (float)(_cumulativeJitter / _jitterSampleCount) : 0,
            LastRttMs = (float)_lastRtt,
            InFlightCount = inFlight
        };
    }
}

public struct QualityReport
{
    public long TotalSent;
    public long TotalReceived;
    public float LossRate;
    public long OutOfOrderCount;
    public float AvgJitterMs;
    public float LastRttMs;
    public long InFlightCount;

    public override string ToString() => 
        $"Sent: {TotalSent} | Recv: {TotalReceived} | Loss: {LossRate:F2}% | Jitter: {AvgJitterMs:F2}ms | RTT: {LastRttMs:F2}ms | OOO: {OutOfOrderCount} | InFlight: {InFlightCount}";
}
