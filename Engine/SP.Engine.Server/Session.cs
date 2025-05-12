using System;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime;
using SP.Engine.Protocol;

namespace SP.Engine.Server
{
    public interface ISession<out TPeer> : ISession
        where TPeer : BasePeer, IPeer
    {
        TPeer Peer { get; }
    }
    
    public class Session<TPeer> : BaseSession<Session<TPeer>>, ISession<TPeer>
        where TPeer : BasePeer, IPeer
    {
        public TPeer Peer { get; private set; }
        public Engine<TPeer> Engine { get; private set; }
        public bool IsClosing { get; private set; }
        public int LatencyAverageMs { get; private set; }
        public int LatencyStandardDeviationMs { get; private set; }
        
        public override void Initialize(IEngine engine, ISocketSession socketSession)
        {
            base.Initialize(engine, socketSession);
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
            TryInternalSend(new S2CEngineProtocolData.Pong { SentTime = sentTime, ServerTime = DateTime.UtcNow });
        }

        public void OnMessageAck(long sequenceNumber)
        {
            Peer?.OnMessageAck(sequenceNumber);
        }
        
        public void SendCloseHandshake()
        {
            if (!IsClosing)
            {
                IsClosing = true;
                StartClosingTime = DateTime.UtcNow;    
                Engine?.EnqueueCloseHandshakePendingQueue(this);
            }
            
            TryInternalSend(new S2CEngineProtocolData.Close());
        }
        
        internal void SendMessageAck(long sequenceNumber)
        {
            TryInternalSend( new S2CEngineProtocolData.MessageAck { SequenceNumber = sequenceNumber });
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
