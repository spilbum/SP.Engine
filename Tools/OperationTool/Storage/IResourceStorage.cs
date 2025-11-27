namespace OperationTool.Storage;

public interface IResourceStorage
{
    Task UploadResourceAsync(int resourceVersion, int fileId, string localPath);
}
