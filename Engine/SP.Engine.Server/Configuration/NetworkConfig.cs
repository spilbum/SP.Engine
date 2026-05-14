namespace SP.Engine.Server.Configuration;

public sealed record NetworkConfig
{
    /// <summary>
    /// 송신 타임아웃
    /// </summary>
    public int SendTimeoutMs { get; init; } = 5_000;
    /// <summary>
    /// 커널 수신 버퍼 크기
    /// </summary>
    public int ReceiveBufferSize { get; init; } = 64 * 1024;
    /// <summary>
    /// 커널 송신 버퍼 크기
    /// </summary>
    public int SendBufferSize { get; init; } = 4 * 1024;
    /// <summary>
    /// 단일 프레임 최대 허용 크기
    /// </summary>
    public int MaxFrameBytes { get; init; } = 64 * 1024;

    /// <summary>
    /// 세션당 동시 전송 가능 큐 크기
    /// </summary>
    public int SendingQueueSize { get; init; } = 5;
    /// <summary>
    /// Keep-alive 사용 여부
    /// </summary>
    public bool EnableKeepAlive { get; init; } = true;
    /// <summary>
    /// Keep-alive 시작 대기 시간
    /// </summary>
    public int KeepAliveTimeSec { get; init; } = 30;
    /// <summary>
    /// Keep-alive 재시도 간격
    /// </summary>
    public int KeepAliveIntervalSec { get; init; } = 2;
    
    /// <summary>
    /// 패킷 암호화 사용 여부
    /// </summary>
    public bool UseEncrypt { get; init; } = true;
    /// <summary>
    /// 패킷 압축 사용 여부
    /// </summary>
    public bool UseCompress { get; init; } = true;
    /// <summary>
    /// 압축을 시도할 최소 패킷 크기 (Bytes)
    /// </summary>
    public ushort CompressionThreshold { get; init; } = 2048;
    
    /// <summary>
    /// 최대 패킷 재전송 횟수
    /// </summary>
    public int MaxRetransmissionCount { get; init; } = 5;
    /// <summary>
    /// Delayed ACK 대기 시간 (송신 데이터가 없을 때)
    /// </summary>
    public int MaxAckDelayMs { get; init; } = 40;
    /// <summary>
    /// ACK 없이 수신 가능한 최대 패킷 수 (도달 시 즉시 ACK)
    /// </summary>
    public int AckFrequency { get; init; } = 10;
    /// <summary>
    /// 순서 어긋난 패킷을 보관할 최대 갯수
    /// </summary>
    public int MaxOutOfOrderCount { get; init; } = 1024;
    
    /// <summary>
    /// UDP 활성화 여부
    /// </summary>
    public bool EnableUdp { get; init; } = true;
    /// <summary>
    /// 최소 전송 단위 (MTU)
    /// </summary>
    public ushort UdpMinMtu { get; init; } = 576;
    /// <summary>
    /// 최대 전송 단위 (MTU)
    /// </summary>
    public ushort UdpMaxMtu { get; init; } = 1200;
    /// <summary>
    /// 조각난 UDP 패킷 정리 주기
    /// </summary>
    public int UdpCleanupPeriodSec { get; init; } = 2;
    /// <summary>
    /// 조각난 패킷 조립 타임아웃
    /// </summary>
    public int UdpAssemblyTimeoutSec { get; init; } = 3;
    /// <summary>
    /// 처리를 기다리는 최대 UDP 메시지 수
    /// </summary>
    public int UdpMaxPendingMessageCount { get; init; } = 100;
}
