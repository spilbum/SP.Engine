using System;
using SP.Engine.Protocol;
using SP.Engine.Runtime;
using SP.Engine.Runtime.Command;
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

            if (protocol.InitialRetransmitTimeoutMs > 0) context.MessageProcessor.SetInitialRetransmitTimeoutMs(protocol.InitialRetransmitTimeoutMs);
            if (protocol.MaxRetransmitCount > 0) context.MessageProcessor.SetMaxRetransmitCount(protocol.MaxRetransmitCount);
            if (protocol.MaxAckDelayMs > 0) context.MessageProcessor.SetMaxAckDelayMs(protocol.MaxAckDelayMs);
            if (protocol.AckFrequency > 0) context.MessageProcessor.SetAckFrequency(protocol.AckFrequency);
            if (protocol.MaxOutOfOrderCount > 0) context.MessageProcessor.SetMaxOutOfOrderCount(protocol.MaxOutOfOrderCount);
            
            if (protocol.UseEncrypt) context.SetupEncryptor(protocol.ServerPublicKey);
            if (protocol.UseCompress) context.SetupCompressor(protocol.MaxPayloadLength);
            context.SetupPolicy(protocol.UseEncrypt, protocol.UseCompress, protocol.CompressionThreshold, protocol.MaxPayloadLength);

            if (protocol.UdpOpenPort > 0)
            {
                if (context.ConnectUdpSocket(protocol.UdpOpenPort))
                {
                    context.SetupFragmentAssembler(
                        protocol.FragmentAssemblerCleanupIntervalSec,
                        protocol.FragmentAssemblerClenupTimeoutSec,
                        protocol.FragmentAssemblerPendingMessageThreshold);
                }
            }
            
            context.SessionAuthCompleted(protocol.SessionId, protocol.PeerId);
        }
    }
}
