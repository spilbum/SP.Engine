using System;
using SP.Engine.Runtime;
using SP.Engine.Protocol;
using SP.Engine.Runtime.Networking;
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
        
        internal bool Send(IProtocolData data)
        {
            switch (data.ProtocolType)
            {
                case EProtocolType.Tcp:
                {
                    var message = new TcpMessage();
                    message.Pack(data,null, null);
                    return Send(message);
                }
                case EProtocolType.Udp:
                {
                    var message = new UdpMessage();              
                    message.SetPeerId(Peer.PeerId);
                    message.Pack(data, null, null);
                    return Send(message);
                }
                default:
                    throw new Exception($"Unknown protocol type: {data.ProtocolType}");
            }
        }
        
        internal void SendPong(long sendTimeMs)
        {
            Send(new EngineProtocolData.S2C.Pong
            {
                SendTimeMs = sendTimeMs, 
                ServerTimeMs = Engine.GetServerTimeMs()
            });
        }
        
        internal void SendCloseHandshake()
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
