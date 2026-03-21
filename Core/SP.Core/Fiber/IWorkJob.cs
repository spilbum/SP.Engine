
namespace SP.Core.Fiber
{
    public interface IWorkJob
    {
        string Name { get; }
        void Invoke();
    }
}
