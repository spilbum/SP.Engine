namespace SP.Rank.Season;

public class RankSeasonOptions
{
    public int RankStoreCapacity { get; init; } = 1_000_000;
    public int RankStoreChunkSize { get; init; } = 10_000;
    public int WorkerCount { get; init; } = 1;
    public int UpdaterIntervalMs { get; init; } = 50;
    public int WorkerUpdateIntervalMs { get; init; } = 50;
    public int OutOfRankCapacity { get; init; } = 10_000;
}
