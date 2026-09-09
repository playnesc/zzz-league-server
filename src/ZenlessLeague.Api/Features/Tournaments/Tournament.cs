namespace ZenlessLeague.Api.Features.Tournaments;

// Not in the legacy Firebase schema — 2v2 doesn't exist there yet.
public enum TournamentRegistrationType { Solo, Team }

// Mirrors zzz-league's TOURNAMENT_STATE (functions/utils/tournamentState.js).
public enum TournamentState { Registration, RegistrationClosed, BracketCreated, Started, AwaitingReview, Complete }

// Mirrors the Firebase `Tournament` interface's scalar fields (zzz-league/src/lib/types.ts)
// so a future migration of old tournaments doesn't need a schema change to add them back.
// `matches`/`winnerId` are excluded — bracket-result concerns, a separate future entity.
public class Tournament
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
    public string? Description { get; set; }

    public DateTimeOffset RegistrationStartDate { get; set; }
    public DateTimeOffset RegistrationEndDate { get; set; }
    public DateTimeOffset TournamentStartDate { get; set; }
    public DateTimeOffset TournamentEndDate { get; set; }

    public int MinCost { get; set; }
    public int MaxCost { get; set; }
    public int MinCharacters { get; set; }
    public int MinTier { get; set; }
    public int MaxTier { get; set; }

    public TournamentState State { get; set; } = TournamentState.Registration;
    public TournamentRegistrationType RegistrationType { get; set; }
    public bool Visible { get; set; } = true;

    // Challonge bracket format (e.g. "single elimination") — legacy field was named `type`.
    public string? BracketType { get; set; }
    public string? ChallongeTournamentId { get; set; }
    public string? ChallongeTournamentUrl { get; set; }
    public string? ChallongeWinnerId { get; set; }
    public string? WinnerId { get; set; } // Firebase-uid-style FK to Player, set once the tournament completes

    public int? OverrideEloChange { get; set; }
    public int? ConsolationMatchesTargetRank { get; set; }
    public string? DivisionGroupId { get; set; }
    public int? DivisionIndex { get; set; }

    public string? DiscordRoleName { get; set; }
    public string? DiscordChannelName { get; set; }
    public string? DiscordRoleId { get; set; }
    public string? DiscordChannelId { get; set; }

    // The legacy RTDB push-key, preserved for traceability and so the Phase 8 migration tool can
    // be re-run idempotently against a partially-migrated database. Null for tournaments created
    // directly on this API (never migrated from Firebase).
    public string? LegacyId { get; set; }
}
