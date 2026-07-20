using System;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace SP.Resource.Web;

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

public interface IRpcRequest<TRes>
{
    int MsgId { get; }
}

public sealed class RpcResult<T>
{
    public bool IsSuccess => Error == RpcError.None && Code == ErrorCode.Success;
    public T? Payload { get; }
    public RpcError Error { get; }
    public ErrorCode Code { get; }
    public string? Message { get; }
    public Exception? Exception { get; }

    private RpcResult(T? payload, RpcError error, ErrorCode code, string? message, Exception? ex)
    {
        Payload = payload;
        Error = error;
        Code = code;
        Message = message;
        Exception = ex;
    }

    public static RpcResult<T> Ok(T payload) => new(payload, RpcError.None, ErrorCode.Success, null, null);
    public static RpcResult<T> Fail(RpcError error, ErrorCode code, string? message, Exception? ex = null) => new(default, error, code, message, ex);
}

public interface IRpc
{
    Task<RpcResult<TRes>> CallAsync<TReq, TRes>(TReq request, CancellationToken ct) where TReq : IRpcRequest<TRes>;
}

public sealed class HttpRpc(HttpClient http, string baseUrl) : IRpc
{
    private static readonly JsonSerializer _serializer = JsonSerializer.CreateDefault();

    private readonly HttpClient _http = http ?? throw new ArgumentNullException(nameof(http));
    private readonly string _baseUrl = !string.IsNullOrWhiteSpace(baseUrl)
        ? baseUrl.TrimEnd('/')
        : throw new ArgumentException("baseUrl must not be null or empty", nameof(baseUrl));

    public async Task<RpcResult<TRes>> CallAsync<TReq, TRes>(TReq request, CancellationToken ct) where TReq : IRpcRequest<TRes>
    {
        var cmd = new JsonCmd<TReq>(request.MsgId, request);
        var json = JsonConvert.SerializeObject(cmd);

        JsonRes<TRes>? res = null;

        try
        {
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var response = await _http.PostAsync($"{_baseUrl}/rpc", content, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return RpcResult<TRes>.Fail(RpcError.TransportError, ErrorCode.Unknown, $"Http Error: {response.StatusCode}");

            await using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
            using var sr = new System.IO.StreamReader(stream);
            using var jr = new JsonTextReader(sr);
            res = _serializer.Deserialize<JsonRes<TRes>>(jr);
        }
        catch (OperationCanceledException e)
        {
            return RpcResult<TRes>.Fail(RpcError.Canceled, ErrorCode.Unknown, "RPC cancelled", e);
        }
        catch (HttpRequestException e)
        {
            return RpcResult<TRes>.Fail(RpcError.TransportError, ErrorCode.Unknown, $"HTTP request failed: {e.Message}", e);
        }
        catch (Exception e)
        {
            return RpcResult<TRes>.Fail(RpcError.TransportError, ErrorCode.Unknown, $"Unexpected transport error: {e.Message}", e);
        }

        if (res == null)
            return RpcResult<TRes>.Fail(RpcError.JsonParseFailed, ErrorCode.Unknown, "response is null or failed to parse");

        if (res.Code != ErrorCode.Success)
            return RpcResult<TRes>.Fail(RpcError.ServerError, res.Code, res.Message ?? "Server error");

        if (res.Payload == null)
            return RpcResult<TRes>.Fail(RpcError.JsonParseFailed, res.Code, "Response payload is null for success result");

        return RpcResult<TRes>.Ok(res.Payload);
    }
}


