using SP.Core.Serialization;

namespace SP.Engine.Runtime.Protocol
{
    public interface INetSerializable
    {
        void Serialize(ref NetWriter writer);
        void Deserialize(ref NetReader reader);
    }
}
