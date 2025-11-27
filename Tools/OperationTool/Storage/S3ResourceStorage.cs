namespace OperationTool.Storage;

public class S3ResourceStorage : IResourceStorage
{
    public async Task UploadResourceAsync(int resourceVersion, int fileId, string localPath)
    {
        await Task.Run(() =>
        {
            Console.WriteLine("Uploading resource to S3. resourceVersion={0}, fileId={1}, localPath={2}",
                resourceVersion, fileId, localPath);
        });
    }
}
