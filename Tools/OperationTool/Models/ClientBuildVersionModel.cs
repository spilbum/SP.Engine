using CloudKit;
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
        set
        {
            if (!SetProperty(ref _beginBuildVersion, value)) return;
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(IsValid));
        }
    }

    public string EndBuildVersion
    {
        get => _endBuildVersion;
        set
        {
            if (!SetProperty(ref _endBuildVersion, value)) return;
            OnPropertyChanged(nameof(IsDirty));
            OnPropertyChanged(nameof(IsValid));
        }
    }

    public string OriginBegin { get; private set; }
    public string OriginEnd { get; private set; }

    public bool IsUpdatable =>
        ServerGroupType is ServerGroupType.Dev or ServerGroupType.Live;
    
    public bool IsPromotable =>
        ServerGroupType is ServerGroupType.Dev or ServerGroupType.QA or ServerGroupType.Stage;
    
    public bool IsDirty =>
        !string.Equals(BeginBuildVersion, OriginBegin, StringComparison.Ordinal) ||
        !string.Equals(EndBuildVersion, OriginEnd, StringComparison.Ordinal);

    public bool IsValid
    {
        get
        {
            if (!BuildVersion.TryParse(BeginBuildVersion, out var b)) return false;
            if (!BuildVersion.TryParse(EndBuildVersion, out var e)) return false;
            return b.CompareTo(e) <= 0;
        }
    }

    public static explicit operator ResourceDb.ClientBuildVersionEntity(ClientBuildVersionModel model)
    {
        return new ResourceDb.ClientBuildVersionEntity
        {
            ServerGroupType = (byte)model.ServerGroupType,
            StoreType = (byte)model.StoreType,
            BeginBuildVersion = model.BeginBuildVersion,
            EndBuildVersion = model.EndBuildVersion
        };
    }

    public ClientBuildVersionModel(
        StoreType storeType,
        ServerGroupType serverGroupType,
        string beginBuildVersion,
        string endBuildVersion)
    {
        ServerGroupType = serverGroupType;
        StoreType = storeType;
        BeginBuildVersion = beginBuildVersion;
        EndBuildVersion = endBuildVersion;
        OriginBegin = BeginBuildVersion;
        OriginEnd = EndBuildVersion;
    }

    public ClientBuildVersionModel(ResourceDb.ClientBuildVersionEntity entity)
    {
        ServerGroupType = (ServerGroupType)entity.ServerGroupType;
        StoreType = (StoreType)entity.StoreType;
        BeginBuildVersion = entity.BeginBuildVersion;
        EndBuildVersion = entity.EndBuildVersion;
        OriginBegin = entity.BeginBuildVersion;
        OriginEnd = entity.EndBuildVersion;
    }

    public void AcceptChanges()
    {
        OriginBegin = BeginBuildVersion;
        OriginEnd = EndBuildVersion;
        OnPropertyChanged(nameof(IsDirty));
    }
}
