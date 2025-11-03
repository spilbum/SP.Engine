namespace SP.Sample.Common
{
    public enum RoomKind : byte
    {
        Normal = 0
    }

    public enum RoomLeaveReason : byte
    {
        None = 0,
        SessionClosed = 1,
        ClientClose = 2,
        TimeOut = 3,
        ServerReject = 4
    }

    public enum ActionKind : byte
    {
        GainScore = 0
    }

    public enum ItemKind : byte
    {
        Coin = 0
    }

    public enum RoomVisibility : byte
    {
        Public = 0,
        Private = 1,
        Unlisted = 2
    }

    public enum ErrorCode : short
    {
        InternalError = -2,
        Unknown = -1,
        Ok = 0,
        InvalidRequest = 1,
        Disconnected = 2,

        RoomFull = 100,
        RoomAlreadyExistsUser = 101,
        RoomNotFound = 102,
        RoomClosed = 103,

        MatchTimeout = 200,
        MatchCanceled = 201,
        MatchAlreadyInProgress = 202,

        RankNotFound = 300
    }

    public enum DbKind : byte
    {
        None = 0,
        Game = 1,
        Rank = 2
    }

    public enum SeasonKind : byte
    {
        None = 0,
        Daily = 1
    }
    
    public enum SeasonState : byte
    {
        /// <summary>
        ///     시작 예약됨
        /// </summary>
        Scheduled = 0,

        /// <summary>
        ///     진행 중
        /// </summary>
        Running = 1,

        /// <summary>
        ///     정산 중
        /// </summary>
        Ending = 2,

        /// <summary>
        ///     종료 됨
        /// </summary>
        Ended = 3,

        /// <summary>
        ///     휴지기
        /// </summary>
        Break = 4
    }

}
