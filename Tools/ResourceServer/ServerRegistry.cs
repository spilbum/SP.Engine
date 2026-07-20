using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using SP.Resource;

namespace SP.ResourceServer;

public sealed class ServerRegistry
{
    // Key: Server ID
    private readonly ConcurrentDictionary<string, ServerSyncInfo> _servers = new();

    // 30초 이상 통신이 없으면 죽은 서버로 간주
    private static readonly TimeSpan DeadServerTimeout = TimeSpan.FromSeconds(30);

    public void Upsert(List<ServerSyncInfo>? syncList)
    {
        if (syncList == null) return;
        
        foreach (var info in syncList)
        {
            // 타임스탬프를 현재 리소스 서버 시간 기준으로 덮어씌움 (클라-서버 시간 오차 방지)
            info.UpdatedUtc = DateTimeOffset.UtcNow;
            _servers[info.Id] = info;
        }
    }

    public ServerConnectInfo? GetBestServer(BuildVersion clientVersion, ServerGroupType groupType = ServerGroupType.Live)
    {
        var now = DateTimeOffset.UtcNow;

        // 1. 죽은 서버 제외, 2. 상태 Online, 3. Major 버전 일치, 4. 그룹 타입 일치
        var availableServers = _servers.Values
            .Where(s => (now - s.UpdatedUtc) <= DeadServerTimeout)
            .Where(s => s.Status == ServerStatus.Online)
            // 클라이언트와 게임 서버의 Major 버전이 같아야만 라우팅
            .Where(s => BuildVersion.TryParse(s.BuildVersion, out var serverVersion) && serverVersion.Major == clientVersion.Major)
            // TODO: 나중에 Dev, Live 등 구분이 필요하면 필터링 (현재는 모두 허용하거나 기획에 맞게 수정)
            // .Where(s => ... groupType)
            .ToList();

        if (availableServers.Count == 0)
            return null;

        // 임시로 무작위(혹은 라운드 로빈) 선택 (나중에는 CCU 기반 등 알고리즘 적용 가능)
        // 일단 첫 번째 서버 반환
        var best = availableServers.First();

        return new ServerConnectInfo(
            best.Id,
            best.Kind,
            best.Region,
            best.Host,
            best.Port,
            best.Status
        );
    }
}
