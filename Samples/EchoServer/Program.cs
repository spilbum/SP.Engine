using System.Runtime.InteropServices;
using Common.Protocol.ES2EC;
using SP.Engine.Server;

namespace EchoServer;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Length < 1 || !int.TryParse(args[0], out var port))
        {
            Console.WriteLine("Usage: EchoServer.exe <port>");
            return;
        }

        await EngineBuilder<EchoServer>.Create()
            .SetCategory("Echo")
            .SetName(nameof(EchoServer))
            .Listen(port)
            .Listen(20000, mode: SocketMode.Udp)
            .AddAssembly(typeof(TcpEchoAck).Assembly)
            .WithEntryAssembly()
            .RunAsync();
    }

    private static async Task RunAsync(this EngineBuilder<EchoServer> builder, CancellationToken cancellationToken = default)
    {
        using var engine = builder.Build();
        await engine.RunAsync(cancellationToken).ConfigureAwait(false);
    }

    private static async Task RunAsync(this EngineBase engine, CancellationToken cancellationToken = default)
    {
        if (!engine.Start())
            throw new InvalidOperationException($"Failed to start engine '{engine.Name}'.");

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        Console.CancelKeyPress += OnCancelKeyPress;
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        using var sigIntReg = TryRegisterPosixSignal(PosixSignal.SIGINT);
        using var sigTermReg = TryRegisterPosixSignal(PosixSignal.SIGTERM);
        using var sigQuitReg = TryRegisterPosixSignal(PosixSignal.SIGQUIT);
        await using var ctReg = cancellationToken.Register(
            static s => ((TaskCompletionSource)s!).TrySetResult(), tcs);

        try
        {
            await tcs.Task.ConfigureAwait(false);
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            AppDomain.CurrentDomain.ProcessExit -= OnProcessExit;
            engine.Stop();
        }

        return;

        void OnProcessExit(object? sender, EventArgs e)
        {
            tcs.TrySetResult();
        }

        void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            tcs.TrySetResult();
        }

        IDisposable? TryRegisterPosixSignal(PosixSignal signal)
        {
            try
            {
                return PosixSignalRegistration.Create(signal, ctx =>
                {
                    ctx.Cancel = true;
                    tcs.TrySetResult();
                });
            }
            catch
            {
                return null;
            }
        }
    }
}
