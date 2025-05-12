using System;
using NetworkCommon;

namespace SP.TestClient
{
    public class Program
    {
        static void Main(string[] args)
        {
            if (NetworkManager.Instance.Initialize())
            {
                NetworkManager.Instance.Logger.Info("Network Manager initialized.");
                NetworkManager.Instance.Connect("192.168.0.109", 3542);
            }
            else
            {
                return;
            }
            
            var connected = false;
            
            while (true)
            {
                NetworkManager.Instance.Update();

                if (!connected && NetworkManager.Instance.Connected)
                {
                    connected = true;
                    NetworkManager.Instance.SendProtocol(new C2SProtocolData.LoginReq { Uid = 1 });
                }
                
                Thread.Sleep(16);
            }
        }
    }
}
