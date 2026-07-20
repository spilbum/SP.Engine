using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace SP.Resource.Web;

public sealed class WebHandler
{
    private readonly HttpClient _http;
    private readonly string _baseUrl;
    private readonly HttpRpc _rpc;

    public WebHandler(HttpClient http, string baseUrl)
    {
        _http = http;

        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out _))
            throw new InvalidOperationException($"Invalid baseUrl: {baseUrl}");

        _baseUrl = baseUrl;
        _rpc = new HttpRpc(_http, _baseUrl);
    }

    public async Task<RpcResult<RefreshResourceServerRes>> RefreshResourceServerAsync(CancellationToken ct = default)
    {
        var req = new RefreshResourceServerReq();

        return await _rpc.CallAsync<RefreshResourceServerReq, RefreshResourceServerRes>(
            req, ct).ConfigureAwait(false);
    }

    public async Task<bool> CheckHealthAsync(CancellationToken ct = default)
    {
        try
        {
            using var res = await _http
                .GetAsync($"{_baseUrl}/healthz", ct).ConfigureAwait(false);

            if (!res.IsSuccessStatusCode)
                return false;

            var text = await res.Content.ReadAsStringAsync().ConfigureAwait(false);
            return string.Equals(text.Trim(), "ok", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
