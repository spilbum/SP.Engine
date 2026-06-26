using SP.Engine.Common.Protocol;
using SP.Engine.Common.Protocol.S2C;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Networking;

namespace SP.Engine.Client.Command
{
    [CommandHandler(ProtocolId.S2C.SessionAuthAck)]
    internal class SessionAuthAckHandler : CommandHandlerBase<NetPeerBase, SessionAuthAck>
    {
        protected override void ExecuteCommand(NetPeerBase context, SessionAuthAck protocol)
        {
            if (protocol.Result != SessionAuthResult.Ok)
            {
                context.Logger.Error("Session authentication failed: {0}", protocol.Result);
                context.Close();
                return;
            }

            if (context.PeerId == 0)
            {
                // 최초 연결 시
                var processor = ReliableMessageProcessor.CreateBuilder()
                    .SetRetransmitPolicy(protocol.ReliableMaxRetransmitCount, protocol.ReliableInitialRetransmitTimeoutMs)
                    .SetAckPolicy(protocol.ReliableMaxAckDelayMs, protocol.ReliableAckFrequency)
                    .SetMaxOutOfOrderCount(protocol.ReliableMaxOutOfOrderCount)
                    .SetPendingQueueCapacity(protocol.ReliablePendingQueueCapacity)
                    .SetInFlightLimit(protocol.ReliableInFlightLimit)
                    .Build();
                context.SetReliableMessageProcessor(processor);
            }
            
            if (protocol.UseEncrypt) context.SetupEncryptor(protocol.EncryptPublicKey);
            if (protocol.UseCompress) context.SetupCompressor(protocol.MaxPayloadLength);
            context.SetupPolicy(protocol.UseEncrypt, protocol.UseCompress, protocol.CompressionThreshold);
            context.SetMaxPayloadLength(protocol.MaxPayloadLength);
            
            if (protocol.UdpOpenPort > 0)
            {
                if (context.ConnectUdpSocket(protocol.UdpOpenPort))
                {
                    context.SetupFragmentAssembler(
                        protocol.FragmentAssemblerCleanupIntervalSec,
                        protocol.FragmentAssemblerCleanupTimeoutSec,
                        protocol.FragmentAssemblerPendingMessageThreshold,
                        protocol.MaxPayloadLength);
                }
            }
            
            context.HandleRemoteAck(protocol.NextExpectedSeq);
            context.SessionAuthCompleted(protocol.SessionId, protocol.PeerId);
        }
    }
}
