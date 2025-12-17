using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SP.Shared.Resource.Web;

public enum RpcError
{
    None = 0,
    ServerError = 1,
    TransportError = 2,
    Canceled = 3,
    JsonParseFailed = 4
}

public class RpcException(
    RpcError error,
    string message,
    ErrorCode? serverError = null,
    Exception? inner = null
) : Exception(message, inner)
{
    public RpcError Error { get; } = error;
    public ErrorCode? ServerError { get; } = serverError;
}

public interface IRpc
{
    Task<TRes> CallAsync<TReq, TRes>(int msgId, TReq payload, CancellationToken ct);
}

public sealed class HttpRpc(HttpClient http, string baseUrl) : IRpc
{
    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly string _baseUrl = !string.IsNullOrWhiteSpace(baseUrl)
        ? baseUrl.TrimEnd('/')
        : throw new ArgumentException("baseUrl must not be null or empty", nameof(baseUrl));
    
    public async Task<TRes> CallAsync<TReq, TRes>(int msgId, TReq payload, CancellationToken ct)
    {
        var cmd = new JsonCmd<TReq>(msgId, payload);
        var json = JsonConvert.SerializeObject(cmd);

        string bodyJson;

        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync($"{_baseUrl}/rpc", content, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new RpcException(RpcError.TransportError, $"Http Error: {response.StatusCode}");

            bodyJson = await response.Content.ReadAsStringAsync();
        }
        catch (OperationCanceledException e)
        {
            throw new RpcException(RpcError.Canceled, "RPC cancelled", inner: e);
        }
        catch (HttpRequestException e)
        {
            throw new RpcException(RpcError.TransportError, $"HTTP request failed: {e.Message}", inner: e);
        }
        catch (Exception e)
        {
            throw new RpcException(RpcError.TransportError, $"Unexpected transport error: {e.Message}", inner: e);
        }

        JsonRes<TRes>? res;
        
        try
        {
            res = JsonConvert.DeserializeObject<JsonRes<TRes>>(bodyJson);
            if (res == null)
                throw new Exception("response is null");
        }
        catch (Exception e)
        {
            throw new RpcException(RpcError.JsonParseFailed, e.Message, inner: e);
        }

        if (res.Code != ErrorCode.Success)
            throw new RpcException(RpcError.ServerError, res.Message ?? "Server error", res.Code);

        if (res.Payload == null)
            throw new RpcException(RpcError.JsonParseFailed,
                "Response payload is null for success result",
                res.Code);
        
        return res.Payload;
    }
}


