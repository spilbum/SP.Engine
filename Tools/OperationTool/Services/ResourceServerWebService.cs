using CommunityToolkit.Maui.Alerts;
using SP.Shared.Resource.Web;

namespace OperationTool.Services;

public class ResourceServerWebService(HttpClient http, IResourceConfigStore configStore)
{
    private WebHandler GetWebHandler()
    {
        var baseUrl = configStore.Get("resource_server_url");
        if (string.IsNullOrEmpty(baseUrl))
            throw new Exception("Resource server URL is missing");
        return new WebHandler(http, baseUrl);
    }

    public async Task RefreshAsync(CancellationToken ct = default)
    {
        try
        {
            var handler = GetWebHandler();
            await handler.RefreshResourceServerAsync(ct);
        }
        catch (RpcException e)
        {
            await Toast.Make($"Failed to refresh resource server: {e.Message}").Show(ct);
        }
    }
}
