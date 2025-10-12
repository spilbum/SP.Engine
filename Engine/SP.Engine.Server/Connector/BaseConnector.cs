using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SP.Common.Logging;
using SP.Engine.Client;
using SP.Engine.Runtime.Protocol;
using SP.Engine.Server.Configuration;
using SP.Engine.Server.ProtocolHandler;
using EngineConfigBuilder = SP.Engine.Client.Configuration.EngineConfigBuilder;

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
        void Send(IProtocol protocol);
    }
    
    public abstract class BaseConnector(string name) : IHandleContext, IServerConnector, IDisposable
    {
        private bool _isDisposed;
        private bool _isOffline;        
        private NetPeer _netPeer;
        private readonly Dictionary<ushort, ProtocolMethodInvoker> _invokers = new();
        
        public event Action Connected;
        public event Action Disconnected;
        public event Action Offline;
        
        public string Name { get; } = name;

        public uint PeerId => _netPeer?.PeerId ?? 0;
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
                _netPeer = CreateNetPeer(config);

                foreach (var invoker in ProtocolMethodInvoker.Load(GetType()))
                {
                    if (!_invokers.TryAdd(invoker.Id, invoker))
                        logger.Warn("Invoker {0} already exists.", invoker.Id);
                }
                
                logger.Debug("Invoker loaded: {0}", string.Join(", ", _invokers.Keys.ToArray()));
                return true;
            }
            catch (Exception e)
            {
                server.Logger.Error(e);
                return false;
            }
        }
        
        private ProtocolMethodInvoker GetInvoker(ushort id)
        {
            _invokers.TryGetValue(id, out var invoker);
            return invoker;
        }

        private NetPeer CreateNetPeer(ConnectorConfig config)
        {
            var engineConfig = EngineConfigBuilder.Create()
                .WithAutoPing(config.EnableAutoPing, config.AutoPingIntervalSec)
                .WithConnectAttempt(config.MaxConnectAttempts, config.ConnectAttemptIntervalSec)
                .WithReconnectAttempt(config.MaxReconnectAttempts, config.ReconnectAttemptIntervalSec)
                .Build();

            var assembly = GetType().Assembly;
            var netPeer = new NetPeer(engineConfig, assembly, Logger);
            netPeer.Connected += OnConnected;
            netPeer.Error += OnError;
            netPeer.Offline += OnOffline;
            netPeer.Disconnected += OnDisconnect;
            netPeer.MessageReceived += OnMessageReceived;
            netPeer.StateChanged += OnStateChanged;
            return netPeer;
        }

        private void OnStateChanged(object sender, StateChangedEventArgs e)
        {
            Log(LogLevel.Info, "State Changed: {0} -> {1}", e.OldState, e.NewState);
        }

        private void OnOffline(object sender, EventArgs e)
        {
            _isOffline = true;
            Log(LogLevel.Info, "Connection to server {0}:{1} has been lost.", Host, Port);
            Offline?.Invoke();
        }

        public void Connect()
        {
            try
            {
                _netPeer?.Connect(Host, Port);
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

        public void Send(IProtocol protocol)
        {
            _netPeer?.Send(protocol);
        }

        public virtual void Update()
        {
            var netPeer = _netPeer;
            if (netPeer == null) return;
            
            netPeer.Tick();
            switch (netPeer.State)
            {
                case NetPeerState.Closed:
                    netPeer.Connect(Host, Port);
                    break;
            }
        }

        private void OnMessageReceived(object sender, MessageReceivedEventArgs e)
        {
            try
            {
                var message = e.Message;
                var invoker = GetInvoker(message.Id);
                if (invoker == null)
                    throw new Exception("Unknown protocol: " + message.Id);
                
                invoker.Invoke(this, message, _netPeer.Encryptor, _netPeer.Compressor);
            }
            catch (Exception ex)
            {
                LogError(ex);
            }
        }

        private void OnDisconnect(object sender, EventArgs e)
        {
            Log(LogLevel.Info, "Disconnected from server {0}:{1}", Host, Port);
            Disconnected?.Invoke();
        }
        

        private void OnError(object sender, ErrorEventArgs e)
        {
            LogError(e.GetException());
        }

        private void OnConnected(object sender, EventArgs e)
        {
            Log(LogLevel.Info, "Connected to server {0}:{1}. peerId={2}", Host, Port, PeerId);
            if (_isOffline)
                _isOffline = false;
            else
                Connected?.Invoke();
        }

        private void Log(LogLevel level, string format, params object[] args)
        {
            Logger.Log(level, format, args);
        }

        private void LogError(Exception ex)
        {
            Logger.Error(ex);
        }
    }
}
