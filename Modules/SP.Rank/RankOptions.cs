namespace SP.Rank;


public class RankOptions
{
    public int MaxRankedCount { get; init; } = 100_000;
    public int ChunkSize { get; init; } = 10_000;
    public float OutOfRankRatio { get; init; } = 0.3f;
    public RankOrder RankOrder { get; init; } = RankOrder.HigherIsBetter;
    public int RecordUpdatesPerTick { get; init; } = 100;
}
