namespace ZenlessLeague.Api.Features.Tournaments;

public enum TournamentRegistrationType { Solo, Team }

public enum TournamentState { Registration, RegistrationClosed, BracketCreated, Started, AwaitingReview, Complete }

// Scalar fields only — bracket-result concerns (`matches`/`winnerId`) are a separate entity
// (TournamentMatch), not stored here.
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

	public string? BracketType { get; set; }
	public string? ChallongeTournamentId { get; set; }
	public string? ChallongeTournamentUrl { get; set; }
	public string? ChallongeWinnerId { get; set; }
	public Guid? WinnerId { get; set; } // FK to Player.Id, set once the tournament completes
	public Guid? WinnerTeamId { get; set; } // team-tournament equivalent of WinnerId, set XOR with it

	public int? OverrideEloChange { get; set; }
	public int? ConsolationMatchesTargetRank { get; set; }
	public string? DivisionGroupId { get; set; }
	public int? DivisionIndex { get; set; }

	public string? DiscordRoleName { get; set; }
	public string? DiscordChannelName { get; set; }
	public string? DiscordRoleId { get; set; }
	public string? DiscordChannelId { get; set; }

	public string? LegacyId { get; set; }
}
