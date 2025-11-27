using ResourceServer.Services;
using SP.Shared.Resource;

namespace ResourceServer.Handlers;

public sealed class CheckClientHandler(PatchPolicyStore store, IServerDirectory dir) : IJsonHandler
{
    public int ReqId => MsgId.CheckClientReq;
    public int ResId => MsgId.CheckClientRes;
    public Type ReqType => typeof(JsonCmd<CheckClientReq>);

    public ValueTask<object> HandleAsync(object req, CancellationToken ct)
    {
        var cmd = (JsonCmd<CheckClientReq>)req;
        var payload = cmd.Payload;
        if (payload == null)
        {
            return ValueTask.FromResult<object>(
                JsonResult.Error(ResId, ErrorCode.InvalidFormat, "payload is null"));
        }

        if (!store.TryGet(payload.Platform, out var info))
        {
            var snap = dir.GetSnapshot();
            var allowRes = new CheckClientRes
            {
                IsAllow = true,
                LatestBuildVersion = payload.BuildVersion,
                LatestResourceVersion = payload.ResourceVersion ?? 0,
                Servers = snap.List.ToList()
            };
            return ValueTask.FromResult<object>(JsonResult.Ok(ResId, allowRes));
        }

        var isForce = CompareVersion(payload.BuildVersion, info.MinVersion) < 0;
        var isSoft = !isForce && CompareVersion(payload.BuildVersion, info.LatestVersion) < 0;
        
        var isPatch = payload.ResourceVersion < info.LatestResourceVersion;
        
        string? manifestUrl = null;

        if (isPatch && !string.IsNullOrEmpty(info.PatchBaseUrl))
        {
            var baseUrl = info.PatchBaseUrl.TrimEnd('/');
            var version = info.LatestResourceVersion;
            manifestUrl = $"{baseUrl}/{version}/manifest.json";
        }
        
        var snapshot = dir.GetSnapshot();
        var res = new CheckClientRes
        {
            IsAllow = !isForce,
            IsForceUpdate = isForce,
            IsSoftUpdate = isSoft,
            LatestBuildVersion = info.LatestVersion,
            LatestResourceVersion = info.LatestResourceVersion,
            ManifestUrl = manifestUrl,
            StoreUrl = info.StoreUrl,
            Servers = snapshot.List.ToList()
        };
        
        return ValueTask.FromResult<object>(JsonResult.Ok(ResId, res));
    }
    
    private static int CompareVersion(string a, string b)
    {
        var va = Normalize(a);
        var vb = Normalize(b);

        for (int i = 0, n = Math.Max(va.Length, vb.Length); i < n; ++i)
        {
            var ai = i < va.Length ? va[i] : 0;
            var bi = i < vb.Length ? vb[i] : 0;
            if (ai != bi) return ai.CompareTo(bi);
        }
        return 0;

        static int[] Normalize(string s) =>
            new string(s.Where(ch => char.IsDigit(ch) || ch == '.').ToArray())
                .Split('.', StringSplitOptions.RemoveEmptyEntries)
                .Select(x => int.TryParse(x, out var n) ? n : 0)
                .DefaultIfEmpty(0)
                .ToArray();
    }
}
