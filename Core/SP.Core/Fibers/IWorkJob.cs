
using System;

namespace SP.Core.Fibers
{
    public interface IWorkJob : IDisposable
    {
        string Name { get; }
        void Execute();
    }
}
