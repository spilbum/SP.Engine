using System;
using System.IO;
using System.Reflection;
using SP.Engine.Client;
using SP.Engine.Common;
using SP.Engine.Common.Logging;
using SP.Engine.Core;
using SP.Engine.Core.Protocol;

namespace SP.Engine.Server.Connector
{

    public interface IConnector
    {
        string Name { get; }
        string Host { get; }
        int Port { get; }
        bool Initialize(IServer server, ConnectorConfig config);
        void Connect();
        void Close();
        void Update();
        void Send(IProtocolData protocol);
    }

    public abstract class ConnectorBase(string name) : IProtocolHandler, IConnector, IDisposable
    {
        private readonly string _name = name;
        private bool _isDisposed;
        private bool _isOffline;        
        private NetPeer _netPeer;
        private ILogger _logger;
        public event EventHandler Connected;
        public event EventHandler Disconnected;

        public string Name => _name;
        public EPeerId PeerId => _netPeer?.PeerId ?? EPeerId.None;
        public string Host { get; private set; }
        public int Port { get; private set; }

        public virtual bool Initialize(IServer server, ConnectorConfig config)
        {
            var logger = server.Logger;
            if (string.IsNullOrEmpty(config.Host) || 0 >= config.Port)
            {
                logger.WriteLog(ELogLevel.Error, "Invalid connector config. host={0}, port={1}", config.Host,
                    config.Port);
                return false;
            }

            Host = config.Host;
            Port = config.Port;
            _logger = logger;

            try
            {
                _netPeer = CreateNetPeer();
                return true;
            }
            catch (Exception e)
            {
                server.Logger.WriteLog(e);
                return false;
            }
        }

        private NetPeer CreateNetPeer()
        {
            var netPeer = new NetPeer(Assembly.GetExecutingAssembly());
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
            WriteLog(ELogLevel.Info, "Connection to server {0}:{1} has been lost.", Host, Port);
        }

        public void Connect()
        {
            try
            {
                _netPeer?.Open(Host, Port);
            }
            catch (Exception ex)
            {
                WriteLog(ex);
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
            _netPeer?.Update();
        }

        private void OnMessageReceived(object sender, MessageEventArgs e)
        {
            try
            {
                var message = e.Message;
                var invoker = ProtocolManager.GetProtocolInvoker(message.ProtocolId);
                if (null == invoker)
                    throw new InvalidOperationException($"The protocol invoker not found. protocolId={message.ProtocolId}");

                invoker.Invoke(this, message, _netPeer?.CryptoSharedKey);
            }
            catch (Exception ex)
            {
                WriteLog(ex);
            }
        }

        private void OnDisconnect(object sender, EventArgs e)
        {
            WriteLog(ELogLevel.Info, "Disconnected from server {0}:{1}", Host, Port);
            Disconnected?.Invoke(this, e);
        }

        private void OnError(object sender, ErrorEventArgs e)
        {
            WriteLog(e.GetException());
        }

        private void OnConnected(object sender, EventArgs e)
        {
            WriteLog(ELogLevel.Info, "Connected to server {0}:{1}. peerId={2}", Host, Port, PeerId);
            if (_isOffline)
                _isOffline = false;
            else
                Connected?.Invoke(this, EventArgs.Empty);
        }

        private void WriteLog(ELogLevel level, string format, params object[] args)
        {
            _logger?.WriteLog(level, format, args);
        }

        private void WriteLog(Exception ex)
        {
            _logger?.WriteLog(ex);
        }
    }
}
