using SP.Engine.Runtime.Command;
using SP.Engine.Runtime.Protocol;
using SP.Sample.Common;
using SP.Sample.GameServer.UserPeer;

namespace SP.Sample.GameServer.Command;

[ProtocolCommand(C2GProtocol.LoginReq)]
public class LoginReq : BaseCommand<GamePeer, C2GProtocolData.LoginReq>
{
    protected override void ExecuteProtocol(GamePeer context, C2GProtocolData.LoginReq protocol)
    {
        var ack = new G2CProtocolData.LoginAck { Result = ErrorCode.Unknown };

        try
        {
            var repo = GameServer.Instance.Repository;

            var uid = protocol.Uid;
            var accessToken = protocol.AccessToken ?? string.Empty;
            var name = protocol.Name ?? "none";
            var countryCode = protocol.CountryCode ?? "--";
            if (uid == 0)
            {
                // 신규 계정
                accessToken = Guid.NewGuid().ToString();
                uid = repo.NewUser(accessToken, name, countryCode);
            }
            else
            {
                // 로그인
                if (!repo.LoginUser(uid, accessToken, name, countryCode))
                    throw new InvalidOperationException($"LoginUser failed. uid={uid}");
            }

            var user = repo.GetUser(uid);
            if (user == null)
                throw new InvalidOperationException($"LoadUser failed. uid={uid}");

            context.SetUid(user.Uid);
            GameServer.Instance.Bind(context);

            ack.Result = ErrorCode.Ok;
            ack.Uid = uid;
            ack.AccessToken = accessToken;
        }
        catch (Exception e)
        {
            context.Logger.Error(e, "Login failed");
            ack.Result = ErrorCode.InternalError;
        }
        finally
        {
            context.Send(ack);
        }
    }
}
