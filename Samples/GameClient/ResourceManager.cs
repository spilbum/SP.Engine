using SP.Shared.Resource;
using SP.Shared.Resource.Web;

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
    
    public async Task<CheckClientRes> BootstrapAsync(
        StoreType storeType,
        string buildVersion,
        int resourceVersion,
        string? deviceId,
        CancellationToken ct)
    {
        var req = new CheckClientReq
        {
            StoreType = storeType,
            BuildVersion = buildVersion,
            ResourceVersion = resourceVersion,
            DeviceId = deviceId
        };

        var response = await _resourceRpc
            .CallAsync<CheckClientReq, CheckClientRes>(ResourceMsgId.CheckClientReq, req, ct);
        return response;
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
