namespace SP.Engine.Server.Configuration;

public sealed record NetworkConfig
{
    /// <summary>
    /// 소켓 송신 타임아웃
    /// </summary>
    public int SendTimeoutMs { get; init; } = 5_000;
    /// <summary>
    /// 소켓 수신 버퍼 크기
    /// </summary>
    public int ReceiveBufferSize { get; init; } = 64 * 1024;
    /// <summary>
    /// 소켓 송신 버퍼 크기
    /// </summary>
    public int SendBufferSize { get; init; } = 4 * 1024;
    /// <summary>
    /// 단일 패킷 최대 허용 크기
    /// </summary>
    public int MaxPayloadLength { get; init; } = 64 * 1024;

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
    public ushort CompressionThreshold { get; init; } = 512;
    
    /// <summary>
    /// 최대 재전송 횟수
    /// </summary>
    public int MaxRetransmitCount { get; init; } = 5;
    /// <summary>
    /// 초기 재전송 타임아웃 
    /// </summary>
    public int InitialRetransmitTimeoutMs { get; init; } = 500;
    /// <summary>
    /// Delayed ACK 대기 시간 (송신 데이터가 없을 때)
    /// </summary>
    public int MaxAckDelayMs { get; init; } = 50;
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
    public ushort UdpMaxMtu { get; init; } = 1400;
    /// <summary>
    /// UDP 파편화 조립기 정리 주기
    /// </summary>
    public int FragmentAssemblerCleanupPeriodSec { get; init; } = 3;
    /// <summary>
    /// UDP 파편화 조립기 정리 타임아웃
    /// </summary>
    public int FragmentAssemblerCleanupTimeoutSec { get; init; } = 3;
    /// <summary>
    /// UDP 파편화 조립기 대기 메시지 임계치
    /// </summary>
    public int FragmentAssemblerPendingMessageThreshold { get; init; } = 100;
}
