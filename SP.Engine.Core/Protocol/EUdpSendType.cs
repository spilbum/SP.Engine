namespace SP.Engine.Core.Protocol
{
    public enum EUdpSendType
    {
        None = 0,
        /// <summary>
        /// UDP 특성을 사용. 재전송x, 오류검출x, 순서 보장x
        /// </summary>
        TypeA,
        /// <summary>
        /// 재전송o, 오류검출o, 순서 보장x
        /// </summary>
        TypeB,
        /// <summary>
        /// TCP와 동일한 신뢰성 보장. 재전송o, 오류검출o, 순서 보장o
        /// </summary>
        TypeC
    }
}
