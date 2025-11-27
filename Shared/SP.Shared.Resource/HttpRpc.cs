using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SP.Shared.Resource;

public interface IRpc
{
    Task<TRes> CallAsync<TReq, TRes>(int msgId, TReq payload, CancellationToken ct);
}

public sealed class HttpRpc(HttpClient http, string url) : IRpc
{
    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly string _url = !string.IsNullOrWhiteSpace(url)
        ? url
        : throw new ArgumentException("url must not be null or empty", nameof(url));
    
    public async Task<TRes> CallAsync<TReq, TRes>(int msgId, TReq payload, CancellationToken ct)
    {
        var cmd = new JsonCmd<TReq> { MsgId = msgId, Payload = payload };
        var json = JsonConvert.SerializeObject(cmd);
        
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var response = await _http.PostAsync($"{_url}/rpc", content, ct).ConfigureAwait(false);
            
        if (!response.IsSuccessStatusCode)
            throw new Exception("Http RPC Error:" + response.StatusCode);

        var body = await response.Content.ReadAsStringAsync();
        var res = JsonConvert.DeserializeObject<JsonRes<TRes>>(body);

        if (res is not { Ok: true })
            throw new Exception(res?.Error?.Message ?? "Unknown RPC Error");
            
        return res.Payload!;
    }
}


