namespace SP.Core.Serialization
{
    public interface INetSerializable
    {
        void Serialize(ref NetWriter writer);
        void Deserialize(ref NetReader reader);
        void Reset();
    }
}
