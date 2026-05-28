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
    public int ReliableMaxRetransmitCount { get; init; } = 5;
    /// <summary>
    /// 초기 재전송 타임아웃 
    /// </summary>
    public int ReliableInitialRetransmitTimeoutMs { get; init; } = 500;
    /// <summary>
    /// Delayed ACK 대기 시간 (송신 데이터가 없을 때)
    /// </summary>
    public int ReliableMaxAckDelayMs { get; init; } = 50;
    /// <summary>
    /// ACK 없이 수신 가능한 최대 패킷 수 (도달 시 즉시 ACK)
    /// </summary>
    public int ReliableAckFrequency { get; init; } = 10;
    /// <summary>
    /// 순서 어긋난 패킷을 보관할 최대 갯수
    /// </summary>
    public int ReliableMaxOutOfOrderCount { get; init; } = 1024;
    /// <summary>
    /// 신뢰성 전송에서 동시에 전송할 수 있는 패킷 수 
    /// </summary>
    public int ReliableInFlightLimit { get; init; } = 2048;
    /// <summary>
    /// 신뢰성 전송에서 오프라인 시 전송 대기할 수 있는 패킷 수
    /// </summary>
    public int ReliablePendingQueueCapacity { get; init; } = 1024;
    
    /// <summary>
    /// 수신 백프레셔 제어 타이아웃
    /// </summary>
    public int ReceivingBackPressureTimeoutSec { get; init; } = 10;
    
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
    
    /// <summary>
    /// TCP 송신 채널의 BoundedChannel 최대 용량
    /// </summary>
    public int TcpSendQueueCapacity { get; init; } = 2048;
    /// <summary>
    /// UDP 송신 채널의 BoundedChannel 최대 용량
    /// 파편화 패킷 유입을 고려하여 TCP보다 크게 설정합니다.
    /// </summary>
    public int UdpSendQueueCapacity { get; init; } = 4096;
}
