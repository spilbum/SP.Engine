namespace SP.Engine.Server
{

    public class ListenerConfig
    {
        public string Ip { get; set; } = "Any";
        public int Port { get; set; }
        public int BackLog { get; set;} = 100;
        public ESocketMode Mode { get; set; } = ESocketMode.Tcp;
    }
    
}
