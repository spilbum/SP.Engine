namespace SP.Engine.Server.Configuration;

public sealed record NetworkConfig
{
    public int SendTimeoutMs { get; init; } = 5_000;
    public int ReceiveBufferSize { get; init; } = 64 * 1024;
    public int SendBufferSize { get; init; } = 4 * 1024;
    public int MaxFrameBytes { get; init; } = 64 * 1024;
    public int LimitConnectionCount { get; init; } = 3_000;
    public int SendingQueueSize { get; init; } = 5;
    public bool EnableKeepAlive { get; init; } = true;
    public int KeepAliveTimeSec { get; init; } = 30;
    public int KeepAliveIntervalSec { get; init; } = 2;
    public bool LogAllSocketError { get; init; }
    public bool UseEncrypt { get; init; } = true;
    public bool UseCompress { get; init; } = false;
    public ushort CompressionThreshold { get; init; } = 2048;
    public int MaxRetryCount { get; init; } = 5;
    public int MinMtu { get; init; } = 576;
    public int MaxMtu { get; init; } = 1500;

    // 상행 패킷이 없을 때, ACK 송신을 최대한 미룰 수 있는 최대 시간 (Delayed ACK)
    public int MaxAckDelayMs { get; init; } = 40;
    // ACK를 보내지 않고 수신할 수 있는 최대 시퀀스 차이
    // 이 수치만큼 패킷이 쌓이면 즉시 ACK를 발송함
    public int AckStepThreshold { get; init; } = 10;
    
}
