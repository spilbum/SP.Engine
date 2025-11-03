namespace SP.Sample.Common
{
    public class RoomOptionsInfo
    {
        public RoomKind Kind;
        public int? MatchDurationSec;
        public int? MaxMembers;
        public int? ReadyCountdownSec;
        public RoomVisibility? Visibility;
    }

    public class RoomMemberInfo
    {
        public string? Name;
        public long UserId;
    }

    public class ItemInfo
    {
        public int ItemId;
        public ItemKind Kind;
        public int Value;
    }

    public class RankProfileInfo
    {
        public string Name { get; set; } = string.Empty;
        public string CountryCode { get; set; } = "--";
        public int Level { get; set; }
    }

    public class PlayerRankInfo
    {
        public long Uid { get; set; }
        public int Rank { get; set; }
        public int Score { get; set; }
        public string Name { get; set; } = string.Empty;
        public string CountryCode { get; set; } = "--";
    }
}
