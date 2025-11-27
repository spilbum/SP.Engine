using OperationTool.DatabaseHandler;
using OperationTool.ViewModels;

namespace OperationTool.Models;

public sealed class RefsFileModel : ViewModelBase
{
    private int _fileId;
    private string _comment = string.Empty;
    private bool _isDevelopment;

    public bool IsDevelopment
    {
        get => _isDevelopment;
        set => SetProperty(ref _isDevelopment, value);
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

    public RefsFileModel(ResourceDb.ResourceRefsFileEntity entity)
    {
        FileId = entity.FileId;
        Comment = entity.Comment ?? string.Empty;
        IsDevelopment = entity.IsDevelopment;
    }
}
