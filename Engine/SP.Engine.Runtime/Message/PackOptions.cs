namespace SP.Engine.Runtime.Message
{
    public class PackOptions
    {
        public bool UseCompression { get; set; } = true;
        public int CompressionThresholdPercent { get; set; } = 10;
        public double CompressionThreshold => 1.0 - CompressionThresholdPercent / 100.0;
        public bool UseEncryption { get; set; } = true;

        public static readonly PackOptions Default = new PackOptions
        {
            UseCompression = true,
            CompressionThresholdPercent = 10,
            UseEncryption = true,
        };
    }
}
