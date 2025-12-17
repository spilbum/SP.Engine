
using SP.Shared.Resource.Web;

namespace ResourceServer;

public interface IJsonHandler
{
    int ReqId { get; }
    int ResId { get; }
    Type ReqType { get; }
    ValueTask<JsonResBase> HandleAsync(object req, CancellationToken ct);
}
