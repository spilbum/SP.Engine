
using System;

namespace SP.Core.Fiber
{
    public interface IWorkJob : IDisposable
    {
        string Name { get; }
        void Execute();
    }
}
