using System;
using NetworkCommon;

namespace SP.TestClient
{
    public class Program
    {
        static void Main(string[] args)
        {
            if (!NetworkManager.Instance.Initialize())
            {
                Console.WriteLine("Failed to initialize");
                return;
            }
            
            NetworkManager.Instance.Connect("192.168.0.109", 3542);

            var connected = false;
            
            while (true)
            {
                NetworkManager.Instance.Update();

                if (!connected && NetworkManager.Instance.Connected)
                {
                    connected = true;
                    var now = DateTime.UtcNow;
                    Console.WriteLine("LoginReq - {0:yyyy-MM-dd hh:mm:ss.fff}", now);
                    NetworkManager.Instance.SendProtocol(new C2SProtocol.Data.LoginReq { Uid = 1, SendTime = now });
                }
                
                Thread.Sleep(16);
            }
        }
    }
}
