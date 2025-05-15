
namespace SampleServer;

internal static class Program
{
    public static void Main(string[] args)
    {
        try
        {
            using var server = new SampleServer();
            if (!server.Setup(args))
                throw new ApplicationException("Failed to setup server");

            while (true)
            {
                Thread.Sleep(100);
            }
        }
        catch (Exception e)
        {
            Console.WriteLine(e);
        }

    }
}

