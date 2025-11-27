namespace ResourceServer.Handlers;

public interface IJsonHandler
{
    int ReqId { get; }
    int ResId { get; }
    Type ReqType { get; }
    ValueTask<object> HandleAsync(object req, CancellationToken ct);
}

