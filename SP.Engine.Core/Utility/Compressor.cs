using System;
using K4os.Compression.LZ4;

namespace SP.Engine.Core.Utility
{
    public static class Compressor
    {
        /// <summary>
        /// 데이터를 압축합니다.
        /// </summary>
        /// <param name="data">원본 데이터</param>
        /// <returns>압축된 데이터</returns>
        public static byte[] Compress(byte[] data)
        {
            if (null == data || 0 == data.Length)
                throw new ArgumentException("data");

            // 원본 길이를 저장할 추가 공간 확보
            int maxOutputSize = LZ4Codec.MaximumOutputSize(data.Length) + sizeof(int);
            byte[] compressedData = new byte[maxOutputSize];

            // 원본 길이를 앞에 저장
            System.Buffer.BlockCopy(BitConverter.GetBytes(data.Length), 0, compressedData, 0, sizeof(int));

            // 데이터 압축
            int compressedSize = LZ4Codec.Encode(data, 0, data.Length, compressedData, sizeof(int), maxOutputSize - sizeof(int));
            Array.Resize(ref compressedData, compressedSize + sizeof(int));
            return compressedData;
        }

        public static byte[] Decompress(byte[] data)
        {
            if (null == data || data.Length <= sizeof(int))
                throw new ArgumentException("data");

            // 원본 길이 읽기
            int originalLength = BitConverter.ToInt32(data, 0);

            byte[] decompressedData = new byte[originalLength];

            // 데이터 압축 해제
            int decodedSize = LZ4Codec.Decode(data, sizeof(int), data.Length - sizeof(int), decompressedData, 0, originalLength);
            if (decodedSize != originalLength)
                throw new InvalidOperationException("The length of the uncompressed data does not match the original length.");

            return decompressedData;
        }
    }
}
