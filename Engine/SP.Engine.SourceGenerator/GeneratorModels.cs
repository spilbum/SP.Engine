using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace System.Runtime.CompilerServices
{
    internal static class IsExternalInit { }
}

namespace SP.Engine.SourceGenerator
{
    // 단일 프로토콜 모델
    internal sealed record ProtocolTypeModel(
        string AssemblyName,
        ushort Id,
        int Channel,
        int Encrypt,
        int Compress,
        string Namespace,
        string TypeName,
        string FullTypeName,
        bool IsPartial,
        List<SerializableMemberModel> Members)
    {
        public bool Equals(ProtocolTypeModel? other)
        {
            if (ReferenceEquals(this, other)) return true;
            if (other is null) return false;
            return AssemblyName == other.AssemblyName &&
                   Id == other.Id &&
                   Channel == other.Channel &&
                   Encrypt == other.Encrypt &&
                   Compress == other.Compress &&
                   Namespace == other.Namespace &&
                   TypeName == other.TypeName &&
                   FullTypeName == other.FullTypeName &&
                   IsPartial == other.IsPartial &&
                   Members.SequenceEqual(other.Members);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = FullTypeName.GetHashCode();
                hash = (hash * 397) ^ Id;
                hash = (hash * 397) ^ Channel;
                hash = (hash * 397) ^ Encrypt;
                hash = (hash * 397) ^ Compress;
                hash = (hash * 397) ^ (IsPartial ? 1 : 0);
                for (var i = 0; i < Members.Count; i++)
                {
                    hash = (hash * 397) ^ Members[i].GetHashCode();
                }
                return hash;
            }
        }
    }
    
    internal sealed record ProtocolPolicyModel(
        string AssemblyName,
        ushort Id,
        int Encrypt,
        int Compress);

    // 멤버 직렬화 정보
    internal sealed record SerializableMemberModel(
        string Name,
        string FullTypeName,
        bool IsSupported,
        bool SerializeGet,
        bool SerializeSet,
        string WriteStatement,
        string ReadStatement,
        string ResetStatement);

    // 프로토콜 핸들러 모델
    internal sealed record ProtocolHandlerModel(
        string AssemblyName,
        ushort Id,
        string HandlerFullTypeName,
        string ContextFullTypeName);

    // 식별자 포맷 헬퍼
    internal static class IdentifierHelper
    {
        public static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name)
            {
                sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            }
            return sb.ToString();
        }
    }
}
