using Common.Protocol.CC2CS;
using SP.Engine.Server;

namespace ChatServer;

internal static class Program
{
    private static readonly CancellationTokenSource CTS = new();
    
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

        var builder = EngineBuilder<ChatServer>.Create()
            .SetCategory("Chat")
            .Listen(port)
            .AddS2SClient("Chat", targetIp, targetPort)
            .AddAssembly(typeof(ChatNotifyReq).Assembly)
            .WithEntryAssembly();

        Console.CancelKeyPress += OnCancelKeyPress;
        
        ChatServer? server = null;
        try
        {
            server = builder.Build();
            if (!server.Start())
            {
                throw new Exception("Failed to start server");
            }
            
            Console.WriteLine("Server '{0}' started.", server.Name);

            await Task.Delay(Timeout.Infinite, CTS.Token);
        }
        catch (TaskCanceledException)
        {

        }
        catch (Exception ex)
        {
            Console.WriteLine("An exception occurred: {0}\nStacktrace: {1}", ex.Message, ex.StackTrace);
        }
        finally
        {
            Console.CancelKeyPress -= OnCancelKeyPress;
            server?.Stop();
            CTS.Dispose();
        }
    }

    private static void OnCancelKeyPress(object? sender, ConsoleCancelEventArgs e)
    {
        e.Cancel = true;

        if (!CTS.IsCancellationRequested)
        {
            CTS.Cancel();
        }
    }
}
