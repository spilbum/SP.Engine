// using System.Security.Cryptography;
//
// namespace SP.Engine.Core.Utility
// {
//     /// <summary>
//     /// 암호 처리기
//     /// </summary>
//     public static class Encryptor
//     {
//         public static byte[] Encrypt(byte[] sharedKey, byte[] data)
//         {
//             using (var aes = Aes.Create())
//             {
//                 aes.Key = sharedKey;
//                 
//                 // 랜덤 IV 생성
//                 aes.GenerateIV();
//                 var iv = aes.IV;
//
//                 using var encryptor = aes.CreateEncryptor();
//                 var encryptedData = encryptor.TransformFinalBlock(data, 0, data.Length);
//
//                 // IV와 암호문 결합
//                 var result = new byte[iv.Length + encryptedData.Length];
//                 System.Buffer.BlockCopy(iv, 0, result, 0, iv.Length);
//                 System.Buffer.BlockCopy(encryptedData, 0, result, iv.Length, encryptedData.Length);
//
//                 return result;
//             }
//         }
//         
//         public static byte[] Decrypt(byte[] sharedKey, byte[] encryptedData)
//         {
//             using var aes = Aes.Create();
//             aes.Key = sharedKey;
//
//             // 암호문에서 IV와 실제 데이터를 분리
//             byte[] iv = new byte[16];
//             byte[] cipherText = new byte[encryptedData.Length - iv.Length];
//             System.Buffer.BlockCopy(encryptedData, 0, iv, 0, iv.Length);
//             System.Buffer.BlockCopy(encryptedData, iv.Length, cipherText, 0, cipherText.Length);
//
//             aes.IV = iv;
//
//             using var decryptor = aes.CreateDecryptor();
//             return decryptor.TransformFinalBlock(cipherText, 0, cipherText.Length);
//         }
//     }
// }
