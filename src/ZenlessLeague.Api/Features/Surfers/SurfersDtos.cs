namespace ZenlessLeague.Api.Features.Surfers;

public record SubmitSurfersScoreRequest(int Score, double DurationSeconds);

// Best is the player's leaderboard score after this run (0 and no Rank if they've never scored).
public record SubmitSurfersScoreResponse(int Best, bool IsNewBest, int? Rank);

// Ties share a rank; the earlier run is listed first.
public record SurfersLeaderboardEntryDto(int Rank, string Uid, string Name, string? Avatar, int Score, DateTimeOffset AchievedAt);

public record SurfersMyScoreDto(int Score, int Rank, DateTimeOffset AchievedAt);
