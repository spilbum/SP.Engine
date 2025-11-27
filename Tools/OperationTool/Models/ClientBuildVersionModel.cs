using OperationTool.DatabaseHandler;
using OperationTool.ViewModels;
using SP.Shared.Resource;

namespace OperationTool.Models;

public sealed class ClientBuildVersionModel : ViewModelBase
{
    private ServerGroupType _serverGroupType;
    private StoreType _storeType;
    private string _beginBuildVersion = string.Empty;
    private string _endBuildVersion = string.Empty;

    public ServerGroupType ServerGroupType
    {
        get => _serverGroupType;
        set => SetProperty(ref _serverGroupType, value);
    }

    public StoreType StoreType
    {
        get => _storeType;
        set => SetProperty(ref _storeType, value);
    }

    public string BeginBuildVersion
    {
        get => _beginBuildVersion;
        set => SetProperty(ref _beginBuildVersion, value);
    }

    public string EndBuildVersion
    {
        get => _endBuildVersion;
        set => SetProperty(ref _endBuildVersion, value);
    }

    public ClientBuildVersionModel(ResourceDb.ClientBuildVersionEntity entity)
    {
        if (!Enum.TryParse(entity.ServerGroupType, out ServerGroupType serverGroupType) ||
            !Enum.TryParse(entity.StoreType, out StoreType storeType))
        {
            throw new ArgumentException("Invalid ServerGroupType or StoreType");
        }
        
        ServerGroupType = serverGroupType;
        StoreType = storeType;
        BeginBuildVersion = entity.BeginBuildVersion;
        EndBuildVersion = entity.EndBuildVersion;
    }
}
