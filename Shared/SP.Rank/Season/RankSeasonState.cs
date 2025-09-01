namespace SP.Rank.Season;

public enum RankSeasonState
{
    /// <summary>
    /// 초기 상태
    /// </summary>
    None = 0,
    /// <summary>
    /// 시작 예약됨
    /// </summary>
    Scheduled,
    /// <summary>
    /// 진행 중
    /// </summary>
    Running,
    /// <summary>
    /// 정산 중
    /// </summary>
    Ending,
    /// <summary>
    /// 종료 됨
    /// </summary>
    Ended,
    /// <summary>
    /// 휴지기
    /// </summary>
    Break,
}
