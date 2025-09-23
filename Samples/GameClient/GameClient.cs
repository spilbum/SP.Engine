using Common;
using SP.Common.Logging;
using SP.Engine.Client;
using SP.Engine.Client.Configuration;
using SP.Engine.Runtime.Handler;
using SP.Engine.Runtime.Protocol;

namespace GameClient;

 public class GameClient
    {
        private NetPeer? _peer;
        private readonly Dictionary<EProtocolId, ProtocolMethodInvoker> _invokerDict = new();
        
        public ILogger Logger => _peer?.Logger ?? throw new NullReferenceException(nameof(_peer));

        public GameClient(ILogger logger, EngineConfig config)
        {
            _peer = new NetPeer(config, logger);
            _peer.Connected += OnConnected;
            _peer.Disconnected += OnDisconnected;
            _peer.Error += OnError;
            _peer.MessageReceived += OnMessageReceived;
            _peer.Offline += OnOffline;
            _peer.StateChanged += OnStateChanged;

            if (!ProtocolMethodInvoker.LoadInvokers(GetType())
                    .All(invoker => _invokerDict.TryAdd(invoker.ProtocolId, invoker)))
            {
                throw new Exception("LoadInvokers failed.");
            }
        }
        
        public void Open(string host, int port)
        {
            _peer?.Connect(host, port);
        }

        public void Close()
        {
            _peer?.Close();
        }

        public void Tick()
        {
            _peer?.Tick();
        }

        public void Send(IProtocolData protocol)
        {
            _peer?.Send(protocol);
        }

        public void SendPing()
        {
            _peer?.SendPing();
        }

        public (int ts, int tr) GetTcpTrafficInfo() => _peer?.GetTcpTrafficInfo() ?? throw new NullReferenceException(nameof(_peer));
        public (int ts, int tr) GetUdpTrafficInfo() => _peer?.GetUdpTrafficInfo() ?? throw new NullReferenceException(nameof(_peer));
        public LatencyStats GetLatencyStats() => _peer?.LatencyStats ?? throw new NullReferenceException(nameof(_peer));
        
        private void OnStateChanged(object? sender, StateChangedEventArgs e)
        {
            _peer?.Logger.Debug("[OnStateChanged] {0} -> {1}", e.OldState, e.NewState);
        }

        private void OnOffline(object? sender, EventArgs e)
        {
            _peer?.Logger.Debug("[OnOffline] Offline...");
        }

        private void OnMessageReceived(object? sender, MessageEventArgs e)
        {
            var message = e.Message;
            if (_invokerDict.TryGetValue(message.ProtocolId, out var invoker))
                invoker.Invoke(this, message, _peer?.Encryptor);
        }

        private void OnError(object? sender, ErrorEventArgs e)
        {
            _peer?.Logger.Error(e.GetException());
        }

        private void OnDisconnected(object? sender, EventArgs e)
        {
            _peer?.Logger.Info("[OnDisconnected] Disconnected...");
        }

        private void OnConnected(object? sender, EventArgs e)
        {
            _peer?.Logger.Info("[OnConnected] Connected...");
        }

        [ProtocolMethod(S2CProtocol.UdpEchoAck)]
        private void OnEchoAck(S2CProtocolData.UdpEchoAck data)
        {
            var rawRtt = Program.GetUnixTimestamp() - data.SentTime;
            _peer?.Logger.Debug($"[OnEchoAck] {rawRtt:F1} ms");
        }
    }
