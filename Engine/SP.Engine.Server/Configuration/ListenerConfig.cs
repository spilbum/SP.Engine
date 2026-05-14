namespace SP.Engine.Server.Configuration;

public sealed record ListenerConfig
{
    /// <summary>
    /// 바인딩할 로컬 IP 주소
    /// </summary>
    public string Ip { get; init; } = "Any";
    /// <summary>
    /// 수신 대기 포트
    /// </summary>
    public int Port { get; init; }
    /// <summary>
    /// OS 커널 수준의 연결 대기 큐 크기
    /// </summary>
    public int BackLog { get; init; } = 1024;
    /// <summary>
    /// 소켓 모드
    /// </summary>
    public SocketMode Mode { get; init; }
}
