namespace ZenlessLeague.Api.Features.Seasons;

public record ResetSeasonRequest(string SeasonName);

public record ArchivedPlayerSnapshotDto(string? Uid, string Name, int Elo, bool IsMidConfirmed, bool IsHighConfirmed);

public record ArchiveDto(Guid Id, string SeasonName, DateTimeOffset CreatedAt, List<ArchivedPlayerSnapshotDto> Players);

public record BackfillResultDto(int UpdatedPlayers);

public record SeasonTimerDto(DateTimeOffset? EndTimestamp);

public record SetSeasonTimerRequest(DateTimeOffset EndTimestamp);
