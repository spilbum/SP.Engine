using System;
using SP.Engine.Client;
using SP.Engine.Runtime.Networking;
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
    }
    
    public abstract class BaseConnector : BaseNetPeer, ICommandContext, IServerConnector
    {
        public string Name { get; private set; }
        public string Host { get; private set; }
        public int Port { get; private set; }
        
        public virtual bool Initialize(IEngine server, ConnectorConfig config)
        {
            var logger = server.Logger;
            if (string.IsNullOrEmpty(config.Host) || 0 >= config.Port)
            {
                logger.Error("Invalid connector config. host={0}, port={1}", config.Host,
                    config.Port);
                return false;
            }

            Name = config.Name;
            Host = config.Host;
            Port = config.Port;

            try
            {
                var engineConfig = EngineConfigBuilder.Create()
                    .WithAutoPing(config.EnableAutoPing, config.AutoPingIntervalSec)
                    .WithConnectAttempt(config.MaxConnectAttempts, config.ConnectAttemptIntervalSec)
                    .WithReconnectAttempt(config.MaxReconnectAttempts, config.ReconnectAttemptIntervalSec)
                    .Build();
                
                base.Initialize(engineConfig, logger);
                return true;
            }
            catch (Exception e)
            {
                server.Logger.Error(e);
                return false;
            }
        }
        
        public void Connect()
        {
            try
            {
                Connect(Host, Port);
            }
            catch (Exception ex)
            {
                Logger.Error(ex);
            }
        }
        
        public virtual void Update()
        {
            Tick();
            
            switch (State)
            {
                // 연결이 끊어졌으면 즉시 연결함
                case NetPeerState.Closed:
                    Connect(Host, Port);
                    break;
            }
        }
        
        TProtocol ICommandContext.Deserialize<TProtocol>(IMessage message)
            => message.Deserialize<TProtocol>(Encryptor, Compressor);
    }
}
