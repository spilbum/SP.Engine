﻿using System;
using SP.Engine.Runtime;
using SP.Engine.Protocol;
using SP.Engine.Runtime.Message;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Server
{
    public interface IClientSession<out TPeer> : IClientSession
        where TPeer : BasePeer, IPeer
    {
        TPeer Peer { get; }
    }
    
    public sealed class ClientSession<TPeer> : BaseClientSession<ClientSession<TPeer>>, IClientSession<TPeer>
        where TPeer : BasePeer, IPeer
    {
        public TPeer Peer { get; private set; }
        public Engine<TPeer> Engine { get; private set; }
        public bool IsClosing { get; private set; }
        public int LatencyAverageMs { get; private set; }
        public int LatencyStandardDeviationMs { get; private set; }
        
        public override void Initialize(IEngine engine, TcpNetworkSession networkSession)
        {
            base.Initialize(engine, networkSession);
            Engine = (Engine<TPeer>)engine;
        }

        public void SetPeer(TPeer peer)
        {
            Peer = peer;
        }

        public override void Close()
        {
            Peer = null;
            base.Close();
        }
        
        protected override void ExecuteMessage(IMessage message)
        {
            Engine.ExecuteMessage(this, message);
        }
        
        public void OnPing(DateTime sentTime, int latencyAvgMs, int latencyStddevMs)
        {
            LatencyAverageMs = latencyAvgMs;
            LatencyStandardDeviationMs = latencyStddevMs;
            SendPong(sentTime);
        }
        
        internal bool Send(IProtocolData data)
        {
            var transport = TransportHelper.Resolve(data);
            switch (transport)
            {
                case ETransport.Tcp:
                {
                    var message = new TcpMessage();
                    message.Pack(data,null, null);
                    return Send(message);
                }
                case ETransport.Udp:
                {
                    var message = new UdpMessage();              
                    message.SetPeerId(Peer.PeerId);
                    message.Pack(data, null, null);
                    return Send(message);
                }
                default:
                    throw new Exception($"Unknown transport: {transport}");
            }
        }
        
        private void SendPong(DateTime sentTime)
        {
            var pong = new EngineProtocolData.S2C.Pong { SentTime = sentTime, ServerTime = DateTime.UtcNow };
            Send(pong);
        }
        
        public void SendCloseHandshake()
        {
            if (!IsClosing)
            {
                IsClosing = true;
                StartClosingTime = DateTime.UtcNow;    
                Engine?.EnqueueCloseHandshakePendingQueue(this);
            }

            var close = new EngineProtocolData.S2C.Close();
            Send(close);
        }
        
        internal void SendMessageAck(long sequenceNumber)
        {
            var messageAck = new EngineProtocolData.S2C.MessageAck { SequenceNumber = sequenceNumber };
            Send(messageAck);
        }

        public override void Close(ECloseReason reason)
        {
            if (reason is ECloseReason.TimeOut || reason is ECloseReason.Rejected)
            {
                SendCloseHandshake();
                return;
            }

            base.Close(reason);
        }
    }
}
