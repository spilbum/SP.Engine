
namespace SharedServer;

internal static class Program
{
    private static void Main(string[] args)
    {
        try
        {
            using var server = new SharedServer();
            if (!server.Setup(args))
                throw new Exception("Failed to setup server");

            while (true)
            {
                Thread.Sleep(50);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

    }
}
