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

            if (protocol.SendTimeoutMs > 0) context.MessageProcessor.SetSendTimeoutMs(protocol.SendTimeoutMs);
            if (protocol.MaxRetries > 0) context.MessageProcessor.SetMaxRetransmissionCount(protocol.MaxRetries);
            if (protocol.MaxAckDelayMs > 0) context.MessageProcessor.SetMaxAckDelayMs(protocol.MaxAckDelayMs);
            if (protocol.AckStepThreshold > 0) context.MessageProcessor.SetAckFrequency(protocol.AckStepThreshold);
            if (protocol.MaxOutOfOrderCount > 0) context.MessageProcessor.SetMaxOutOfOrder(protocol.MaxOutOfOrderCount);
            if (protocol.MaxFrameBytes > 0) context.SetMaxFrameSize(protocol.MaxFrameBytes);
            
            if (protocol.UseEncrypt) context.SetupEncryptor(protocol.ServerPublicKey);
            if (protocol.UseCompress) context.SetupCompressor(protocol.MaxFrameBytes);
            
            context.SetupPolicy(protocol.UseEncrypt, protocol.UseCompress, protocol.CompressionThreshold);

            if (protocol.UdpOpenPort > 0)
            {
                context.ConnectUdpSocket(
                    protocol.UdpOpenPort,
                    protocol.UdpAssemblyTimeoutSec, 
                    protocol.UdpMaxPendingMessageCount,
                    protocol.UdpCleanupIntervalSec);
            }
            
            context.SessionAuthCompleted(protocol.SessionId, protocol.PeerId);
        }
    }
}
