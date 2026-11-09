using System.Runtime.InteropServices;
using Common.Protocol.CC2CS;
using SP.Engine.Server;

namespace ChatServer;

internal static class Program
{
    private static async Task Main(string[] args)
    {
        if (args.Length < 3 
            || !int.TryParse(args[0], out var port)
            || string.IsNullOrWhiteSpace(args[1])
            || !int.TryParse(args[2], out var targetPort))
        {
            Console.WriteLine("Usage: ChatServer <port> <target_ip> <target_port>");
            return;
        }

        var targetIp = args[1];

        await EngineBuilder<ChatServer>.Create()
            .SetCategory("Chat")
            .Listen(port)
            .AddS2SClient("Chat", targetIp, targetPort)
            .AddAssembly(typeof(ChatNotifyReq).Assembly)
            .WithEntryAssembly()
            .RunAsync();
    }
    
     private static async Task RunAsync(this EngineBuilder<ChatServer> builder, CancellationToken cancellationToken = default)
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
