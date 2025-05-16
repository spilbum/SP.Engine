using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SP.Common.Logging;
using SP.Engine.Client;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Handler;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.Handler;

namespace SP.Engine.Server.Connector
{

    public interface IServerConnector
    {
        string Name { get; }
        string Host { get; }
        int Port { get; }
        bool Initialize(IEngine server, ConnectorConfig config);
        void Connect();
        void Close();
        void Update();
        void Send(IProtocolData protocol);
    }
    
    public abstract class BaseServerConnector(string name) : IHandleContext, IServerConnector, IDisposable
    {
        private bool _isDisposed;
        private bool _isOffline;        
        private NetPeer _netPeer;
        private readonly Dictionary<EProtocolId, ProtocolMethodInvoker> _invokerDict = new();
        
        public event Action Connected;
        public event Action Disconnected;
        public event Action Offline;
        
        public string Name { get; } = name;

        public EPeerId PeerId => _netPeer?.PeerId ?? EPeerId.None;
        public string Host { get; private set; }
        public int Port { get; private set; }
        public ILogger Logger { get; private set; }

        public virtual bool Initialize(IEngine server, ConnectorConfig config)
        {
            var logger = server.Logger;
            if (string.IsNullOrEmpty(config.Host) || 0 >= config.Port)
            {
                logger.Error("Invalid connector config. host={0}, port={1}", config.Host,
                    config.Port);
                return false;
            }

            Host = config.Host;
            Port = config.Port;
            Logger = logger;

            try
            {
                _netPeer = CreateNetPeer();

                if (!ProtocolMethodInvoker.LoadInvokers(GetType()).All(RegisterInvoker))
                    return false;
                
                Logger.Debug("Invoker loaded: {0}", string.Join(", ", _invokerDict.Keys.ToArray()));
                return true;
            }
            catch (Exception e)
            {
                server.Logger.Error(e);
                return false;
            }
        }

        private bool RegisterInvoker(ProtocolMethodInvoker invoker)
        {
            if (_invokerDict.TryAdd(invoker.ProtocolId, invoker)) return true;
            Logger.Error("Invoker '{0}' already exists.", invoker.ProtocolId);
            return false;
        }

        private ProtocolMethodInvoker GetInvoker(EProtocolId protocolId)
        {
            _invokerDict.TryGetValue(protocolId, out var invoker);
            return invoker;
        }

        private NetPeer CreateNetPeer()
        {
            var netPeer = new NetPeer(Logger);
            netPeer.IsEnableAutoSendPing = true;
            netPeer.AutoSendPingIntervalSec = 10;
            netPeer.MaxConnectionAttempts = -1;
            netPeer.LimitRequestLength = 64 * 1024;
            netPeer.Connected += OnConnected;
            netPeer.Error += OnError;
            netPeer.Offline += OnOffline;
            netPeer.Disconnected += OnDisconnect;
            netPeer.MessageReceived += OnMessageReceived;
            return netPeer;
        }

        private void OnOffline(object sender, EventArgs e)
        {
            _isOffline = true;
            Log(ELogLevel.Info, "Connection to server {0}:{1} has been lost.", Host, Port);
            Offline?.Invoke();
        }

        public void Connect()
        {
            try
            {
                _netPeer?.Open(Host, Port);
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        public void Close()
        {
            _netPeer?.Close();
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (_isDisposed)
                return;

            if (disposing)
            {
                var netPeer = _netPeer;
                if (netPeer != null)
                {
                    netPeer.Connected -= OnConnected;
                    netPeer.Error -= OnError;
                    netPeer.Offline -= OnOffline;
                    netPeer.Disconnected -= OnDisconnect;
                    netPeer.MessageReceived -= OnMessageReceived;
                    netPeer.Dispose();
                    _netPeer = null;
                }
            }

            _isDisposed = true;
        }

        public void Send(IProtocolData protocol)
        {
            _netPeer?.Send(protocol);
        }

        public virtual void Update()
        {
            var netPeer = _netPeer;
            if (netPeer == null) return;
            
            netPeer.Update();
            switch (netPeer.State)
            {
                case ENetPeerState.None:
                case ENetPeerState.Connecting:
                case ENetPeerState.Open:
                case ENetPeerState.Closing:
                    break;
                case ENetPeerState.Closed:
                    netPeer.Open(Host, Port);
                    break;
                default:
                    throw new ArgumentOutOfRangeException();
            }
        }

        private void OnMessageReceived(object sender, MessageEventArgs e)
        {
            try
            {
                var message = e.Message;
                var invoker = GetInvoker(message.ProtocolId);
                if (invoker == null)
                    throw new Exception("Unknown protocol: " + message.ProtocolId);
                
                invoker.Invoke(this, message, _netPeer.DhSharedKey);
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        private void OnDisconnect(object sender, EventArgs e)
        {
            Log(ELogLevel.Info, "Disconnected from server {0}:{1}", Host, Port);
            Disconnected?.Invoke();
        }
        

        private void OnError(object sender, ErrorEventArgs e)
        {
            LogError(e.GetException());
        }

        private void OnConnected(object sender, EventArgs e)
        {
            Log(ELogLevel.Info, "Connected to server {0}:{1}. peerId={2}", Host, Port, PeerId);
            if (_isOffline)
                _isOffline = false;
            else
                Connected?.Invoke();
        }

        private void Log(ELogLevel level, string format, params object[] args)
        {
            Logger.Log(level, format, args);
        }

        private void LogError(Exception ex)
        {
            Logger.Error(ex);
        }
    }
}
