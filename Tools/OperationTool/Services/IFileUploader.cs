namespace OperationTool.Services;

public interface IFileUploader
{
    Task UploadAsync(string key, Stream stream, long length, CancellationToken ct = default);
}
