using OperationTool.ViewModels;
using SP.Shared.Resource;

namespace OperationTool.Models;

public sealed class TableTargetModel : ViewModelBase
{
    public string TableName { get; set; }
    
    private PatchTarget _target;

    public PatchTarget Target
    {
        get => _target;
        set
        {
            if (SetProperty(ref _target, value))
                OnPropertyChanged(nameof(IsDirty));
        }
    }
    
    public PatchTarget Origin { get; private set; }
    public bool IsDirty => Target != Origin;

    public void MarkSaved() => Origin = Target;

    public TableTargetModel(string name, PatchTarget target)
    {
        TableName = name;
        Target = target;
        Origin = target;
    }
}
