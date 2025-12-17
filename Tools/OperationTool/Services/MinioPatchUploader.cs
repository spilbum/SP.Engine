using System.Net;
using Minio;
using Minio.DataModel.Args;

namespace OperationTool.Services;

public class MinioUploader(IResourceConfigStore configStore) : IFileUploader
{
    private static HttpClient CreateHttpClient()
    {
        var handler = new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };
        
        return new HttpClient(handler, disposeHandler: true);
    }
    
    private static IMinioClient CreateClient(
        string? endpoint,
        string? accessKey,
        string? secretKey,
        string? region,
        bool useSSL)
    {

            
        var http = CreateHttpClient();
        
        return new MinioClient()
            .WithEndpoint(endpoint)
            .WithCredentials(accessKey, secretKey)
            .WithRegion(region)
            .WithSSL(useSSL)
            .WithHttpClient(http)
            .Build();
    }

    public async Task UploadAsync(string key, Stream stream, long length,
        CancellationToken ct = default)
    {
        var endpoint = configStore.Get("minio_endpoint");
        var accessKey = configStore.Get("minio_access_key");
        var secretKey = configStore.Get("minio_secret_key");
        var region = configStore.Get("minio_region");
        var useSSL = configStore.GetBool("minio_use_ssl");
        var bucket = configStore.Get("minio_bucket");
        
        using var minio = CreateClient(
            endpoint,
            accessKey,
            secretKey,
            region,
            useSSL);

        var args = new PutObjectArgs()
            .WithBucket(bucket)
            .WithObject(key)
            .WithStreamData(stream)
            .WithObjectSize(length)
            .WithContentType("application/octet-stream");
        
        await minio.PutObjectAsync(args, ct);
    }
}
