namespace RankServer.ServerPeer;

public class GameServerPeer(BaseServerPeer peer, int processId) : BaseServerPeer(peer)
{
    public int ProcessId { get; } = processId;
    public string Host { get; set; } = "127.0.0.1";
    public int Port { get; set; }
    public string BuildVersion { get; set; } = "1.0.0.000001";
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}
