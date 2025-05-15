using System;
using NetworkCommon;

namespace SampleClient
{
    public static class Program
    {
        private static void Main()
        {
            if (NetworkManager.Instance.Initialize())
            {
                NetworkManager.Instance.Logger.Info("Network Manager initialized.");
                NetworkManager.Instance.Connect("127.0.0.1", 10000);
            }
            else
            {
                return;
            }
            
            while (true)
            {
                NetworkManager.Instance.Update();

                if (Console.KeyAvailable)
                {
                    var input = Console.ReadLine();
                    if (string.IsNullOrWhiteSpace(input))
                        continue;
                
                    var tokens = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    if (tokens.Length == 0)
                        continue;
                
                    var command = tokens[0];
                    var args = tokens.Length > 1 ? tokens[1..] : [];

                    if (command.Equals("exit", StringComparison.OrdinalIgnoreCase))
                        break;

                    switch (command.ToLower())
                    {
                        case "echo":
                            HandleEcho(args);
                            break;
                        default:
                            NetworkManager.Instance.Logger.Error("Invalid command: {0}", command);
                            break;
                    }
                }

                Tick();
                Thread.Sleep(50);
            }
        }

        private static void HandleEcho(string[] args)
        {
            if (args.Length == 0)
            {
                NetworkManager.Instance.Logger.Error("Usage: echo [message]");
                return;
            }
            
            var echoReq = new ProtocolData.C2S.EchoReq
            {
                Message = args[0]
            };
            
            NetworkManager.Instance.SendProtocol(echoReq);
        }

        private static void Tick()
        {
            NetworkManager.Instance.Update();
        }
    }
}
