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
            if (SetProperty(ref _beginBuildVersion, value))
                NotifyActionStateChanged();
        }
    }

    public string EndBuildVersion
    {
        get => _endBuildVersion;
        set
        {
            if (SetProperty(ref _endBuildVersion, value)) 
                NotifyActionStateChanged();
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

    public bool CanSave => IsUpdatable && IsDirty && IsValid;

    public bool CanPromote =>
        IsPromotable &&
        !IsDirty;

    public string PrimaryActionText
    {
        get
        {
            if (CanSave) return "Apply";

            return ServerGroupType switch
            {
                ServerGroupType.Dev => "Dev → QA",
                ServerGroupType.QA => "QA → Stage",
                ServerGroupType.Stage => "Stage → Live",
                _ => string.Empty
            };
        }
    }

    public bool IsPrimaryActionVisible => CanSave || CanPromote;
    
    public static explicit operator ResourceDb.ClientBuildVersionEntity(ClientBuildVersionModel model)
    {
        return new ResourceDb.ClientBuildVersionEntity
        {
            ServerGroupType = model.ServerGroupType.ToString(),
            StoreType = model.StoreType.ToString(),
            BeginBuildVersion = model.BeginBuildVersion,
            EndBuildVersion = model.EndBuildVersion,
            ServerGroupOrder = Utils.ToOrder(model.ServerGroupType)
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
        if (!Enum.TryParse(entity.ServerGroupType, out ServerGroupType serverGroupType))
            throw new ArgumentException($"Invalid server group type:{entity.ServerGroupType}");
        
        if (!Enum.TryParse(entity.StoreType, out StoreType storeType))
            throw new ArgumentException($"Invalid store type:{entity.StoreType}");
        
        ServerGroupType = serverGroupType;
        StoreType = storeType;
        BeginBuildVersion = entity.BeginBuildVersion;
        EndBuildVersion = entity.EndBuildVersion;
        OriginBegin = entity.BeginBuildVersion;
        OriginEnd = entity.EndBuildVersion;
    }

    public void MarkSaved()
    {
        OriginBegin = BeginBuildVersion;
        OriginEnd = EndBuildVersion;
        NotifyActionStateChanged();
    }

    private void NotifyActionStateChanged()
    {
        OnPropertyChanged(nameof(IsDirty));
        OnPropertyChanged(nameof(IsValid));
        OnPropertyChanged(nameof(CanSave));
        OnPropertyChanged(nameof(CanPromote));
        OnPropertyChanged(nameof(PrimaryActionText));
        OnPropertyChanged(nameof(IsPrimaryActionVisible));
    }
}
