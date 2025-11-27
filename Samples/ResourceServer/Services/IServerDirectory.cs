using SP.Shared.Resource;

namespace ResourceServer.Services;

public interface IServerDirectory
{
    ServerSnapshot GetSnapshot();
    void ReplaceAll(IEnumerable<ServerSyncInfo> infos, DateTimeOffset updatedUtc);   
}
