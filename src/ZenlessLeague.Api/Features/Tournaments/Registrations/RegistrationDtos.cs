namespace ZenlessLeague.Api.Features.Tournaments;

public record PlayerRegistrationDetailsRequest(
	string GameUid,
	bool PrizeAsMoney,
	string? PrizeUid,
	string DarteNickname,
	string DartePresetName,
	string RosterName,
	string? RosterScreenshot = null,
	string? HoyolabScreenshot = null);

public record SoloRegistrationRequest(string PlayerId, PlayerRegistrationDetailsRequest Details);

public record TeamRegistrationRequest(Guid TeamId, PlayerRegistrationDetailsRequest Player1, PlayerRegistrationDetailsRequest Player2);

public record PlayerRegistrationDetailsDto(
	string GameUid,
	bool PrizeAsMoney,
	string? PrizeUid,
	string DarteNickname,
	string DartePresetName,
	string RosterName,
	string? RosterScreenshotUrl,
	string? HoyolabScreenshotUrl);

public record AdminAddRegistrationRequest(string PlayerId);

public record SetApprovedRequest(bool Approved);

public record TournamentRegistrationDto(
	Guid Id,
	Guid TournamentId,
	TournamentRegistrationType Type,
	string? PlayerId,
	Guid? TeamId,
	PlayerRegistrationDetailsDto Player1,
	PlayerRegistrationDetailsDto? Player2,
	bool Approved,
	DateTimeOffset RegisteredAt,
	DateTimeOffset UpdatedAt);
