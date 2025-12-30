using OperationTool.DatabaseHandler;
using OperationTool.ViewModels;
using SP.Shared.Resource;

namespace OperationTool.Models;

public sealed class ResourcePatchVersionModel : ViewModelBase
{
    private ServerGroupType _serverGroupType;
    private int _resourceVersion;
    private int _targetMajor;
    private int _fileId;
    private string _comment = string.Empty;
    private DateTime _createdUtc;

    public ServerGroupType ServerGroupType
    {
        get => _serverGroupType;
        private set => SetProperty(ref _serverGroupType, value);
    }

    public int ResourceVersion
    {
        get => _resourceVersion;
        private set => SetProperty(ref _resourceVersion, value);
    }

    public int TargetMajor
    {
        get => _targetMajor;
        private set => SetProperty(ref _targetMajor, value);
    }

    public int FileId
    {
        get => _fileId;
        private set => SetProperty(ref _fileId, value);
    }

    public string Comment
    {
        get => _comment;
        private set => SetProperty(ref _comment, value);
    }

    public DateTime CreatedUtc
    {
        get => _createdUtc;
        private set => SetProperty(ref _createdUtc, value);
    }

    public ResourcePatchVersionModel(ResourceDb.ResourcePatchVersionEntity entity)
    {
        if (!Enum.TryParse(entity.ServerGroupType, out ServerGroupType serverGroupType))
            throw new ArgumentException("Invalid ServerGroupType");
        
        ServerGroupType = serverGroupType;
        ResourceVersion = entity.ResourceVersion;
        TargetMajor = entity.TargetMajor;
        FileId = entity.FileId;
        Comment = entity.Comment ?? string.Empty;
        CreatedUtc = entity.CreatedUtc;
    }
}
