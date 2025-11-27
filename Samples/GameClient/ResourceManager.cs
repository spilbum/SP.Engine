using SP.Shared.Resource;

namespace GameClient;

public sealed class ResourceManager : IDisposable
{
    private readonly HttpClient _http;
    private readonly IRpc _resourceRpc;

    public ResourceManager(string resourceServerUrl)
    {
        if (string.IsNullOrWhiteSpace(resourceServerUrl))
            throw new ArgumentNullException(nameof(resourceServerUrl));

        _http = new HttpClient();
        _resourceRpc = new HttpRpc(_http, resourceServerUrl);
    }
    
    public CheckClientRes Bootstrap(
        PlatformKind platform,
        string buildVersion,
        int resourceVersion,
        int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        var req = new CheckClientReq
        {
            Platform = platform,
            BuildVersion = buildVersion,
            ResourceVersion = resourceVersion,
        };
        
        var res = _resourceRpc
            .CallAsync<CheckClientReq, CheckClientRes>(MsgId.CheckClientReq, req, cts.Token)
            .GetAwaiter()
            .GetResult();
        return res;
    }

    public TRes Call<TReq, TRes>(
        int msgId,
        TReq req,
        int timeoutMs = 5000)
    {
        using var cts = new CancellationTokenSource(timeoutMs);
        
        var task = _resourceRpc.CallAsync<TReq, TRes>(msgId, req, cts.Token);
        return task.ConfigureAwait(false).GetAwaiter().GetResult();
    }

    public void Dispose()
    {
        _http.Dispose();
    }
}
