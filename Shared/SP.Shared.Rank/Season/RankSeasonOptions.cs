namespace SP.Shared.Rank.Season;

public enum RankOrder
{
    HigherIsBetter,
    LowerIsBetter
}

public class RankSeasonOptions
{
    public int RankedCapacity { get; init; } = 100_000;
    public int ChunkSize { get; init; } = 10_000;
    public float OutOfRankRatio { get; init; } = 0.3f;
    public RankOrder RankOrder { get; init; } = RankOrder.HigherIsBetter;
    public int MaxUpdatesPerTick { get; init; } = 100;
}
