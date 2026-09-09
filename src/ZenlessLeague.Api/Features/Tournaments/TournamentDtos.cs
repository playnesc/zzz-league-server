namespace ZenlessLeague.Api.Features.Tournaments;

public record CreateTournamentRequest(
    string Name,
    string? Description,
    DateTimeOffset RegistrationStartDate,
    DateTimeOffset RegistrationEndDate,
    DateTimeOffset TournamentStartDate,
    DateTimeOffset TournamentEndDate,
    int MinCost,
    int MaxCost,
    int MinCharacters,
    int MinTier,
    int MaxTier,
    TournamentRegistrationType RegistrationType,
    string? BracketType = null,
    bool Visible = true);

public record TournamentDto(
    Guid Id,
    string Name,
    string? Description,
    DateTimeOffset RegistrationStartDate,
    DateTimeOffset RegistrationEndDate,
    DateTimeOffset TournamentStartDate,
    DateTimeOffset TournamentEndDate,
    int MinCost,
    int MaxCost,
    int MinCharacters,
    int MinTier,
    int MaxTier,
    TournamentState State,
    TournamentRegistrationType RegistrationType,
    bool Visible,
    string? BracketType,
    string? ChallongeTournamentId,
    string? ChallongeTournamentUrl,
    string? ChallongeWinnerId,
    string? WinnerId,
    string? DivisionGroupId,
    int? DivisionIndex);

public record SplitTournamentRequest(List<List<string>> DivisionGroups);

public record SplitTournamentResult(List<Guid> DivisionTournamentIds);
