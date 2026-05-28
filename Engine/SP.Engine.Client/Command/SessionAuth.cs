using System;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Networking;
using SP.Engine.Runtime.Protocol;

namespace SP.Engine.Client.Command
{
    [ProtocolCommand(S2CEngineProtocolId.SessionAuthAck)]
    public class SessionAuth : CommandBase<NetPeerBase, S2CEngineProtocolData.SessionAuthAck>
    {
        protected override void ExecuteCommand(NetPeerBase context, S2CEngineProtocolData.SessionAuthAck protocol)
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
            
            if (protocol.UseEncrypt) context.SetupEncryptor(protocol.ServerPublicKey);
            if (protocol.UseCompress) context.SetupCompressor(protocol.MaxPayloadLength);
            context.SetupPolicy(protocol.UseEncrypt, protocol.UseCompress, protocol.CompressionThreshold, protocol.MaxPayloadLength);

            if (protocol.UdpOpenPort > 0)
            {
                if (context.ConnectUdpSocket(protocol.UdpOpenPort))
                {
                    context.SetupFragmentAssembler(
                        protocol.FragmentAssemblerCleanupIntervalSec,
                        protocol.FragmentAssemblerCleanupTimeoutSec,
                        protocol.FragmentAssemblerPendingMessageThreshold);
                }
            }
            
            context.HandleRemoteAck(protocol.ServerNextExpectedSeq);
            context.SessionAuthCompleted(protocol.SessionId, protocol.PeerId);
        }
    }
}
