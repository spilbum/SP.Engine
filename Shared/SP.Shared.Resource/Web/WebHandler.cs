using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SP.Shared.Resource.Web;

public sealed class WebHandler
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    
    public WebHandler(HttpClient http, string baseUrl)
    {
        _http = http;
        
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException($"Invalid baseUrl: {baseUrl}");
        
        _baseUrl = baseUrl;
    }
    private HttpRpc CreateRpc()
        => new(_http, _baseUrl);
    
    public async Task<RefreshResourceServerRes> RefreshResourceServerAsync(CancellationToken ct = default)
    {
        var rpc = CreateRpc();
        var req = new RefreshResourceServerReq();

        return await rpc.CallAsync<RefreshResourceServerReq, RefreshResourceServerRes>(
            ResourceMsgId.RefreshResourceServerReq, req, ct);
    }
    
    public async Task<bool> CheckHealthAsync(CancellationToken ct = default)
    {
        try
        {
            using var res = await _http
                .GetAsync($"{_baseUrl}/healthz", ct);

            if (!res.IsSuccessStatusCode)
                return false;

            var text = await res.Content.ReadAsStringAsync();
            return string.Equals(text.Trim(), "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
