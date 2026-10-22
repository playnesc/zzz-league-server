using ZenlessLeague.Api.Features.Nexus;
using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Features.Tournaments.Matches;

public sealed record LoadedNexusResult(string ResultId, string Json, NexusParsedResult Parsed, bool? P1IsLeft);

// Turns a pasted Nexus result link into a solo match's result: fetches and parses it, and works out
// which Nexus side is the match's P1.
public class NexusResultResolver(
	INexusClient nexusClient,
	ITournamentRegistrationRepository registrationRepository,
	IPlayerRepository playerRepository,
	ILogger<NexusResultResolver> logger)
{
	// An explicit "left"/"right" from the caller wins, otherwise the names on Nexus are matched
	// against the two players. P1IsLeft stays null when neither applies.
	public async Task<LoadedNexusResult> LoadAsync(Tournament tournament, TournamentMatch match, string url, string? p1Side, CancellationToken ct = default)
	{
		if (!NexusLink.TryParseId(url, out var resultId))
		{
			throw new NexusResultException("That's not a Nexus result link (https://nexus-shiyu.com/results/…).");
		}

		if (match.P1PlayerId is not Guid p1Id || match.P2PlayerId is not Guid p2Id)
		{
			throw new MatchValidationException("Match does not have two known participants yet.");
		}

		var json = await nexusClient.GetResultJsonAsync(resultId, ct);
		NexusParsedResult parsed;
		try
		{
			parsed = NexusResultParser.Parse(json, tournament.GameMode);
		}
		catch (NexusFormatException ex)
		{
			// Nexus's API is undocumented and changes often — the raw response is what an admin
			// needs to update NexusResultParser (Coolify → API app → Logs).
			logger.LogError(ex, "Couldn't parse Nexus result {ResultId}; Nexus may have changed its format. Response: {Response}",
				resultId, json.Length > 8000 ? json[..8000] + "…" : json);
			throw;
		}

		var p1IsLeft = p1Side?.Trim().ToLowerInvariant() switch
		{
			"left" => true,
			"right" => false,
			null or "" => await MatchSidesAsync(tournament.Id, p1Id, p2Id, parsed, ct),
			_ => throw new MatchValidationException("nexusP1Side must be \"left\" or \"right\"."),
		};

		return new LoadedNexusResult(resultId, json, parsed, p1IsLeft);
	}

	public static (string? ResultP1, string? ResultP2) MapTotals(NexusParsedResult parsed, bool? p1IsLeft) => p1IsLeft switch
	{
		true => (parsed.LeftTotal, parsed.RightTotal),
		false => (parsed.RightTotal, parsed.LeftTotal),
		null => (null, null),
	};

	// Shape check only, nothing fetched — for when the link is stored as proof rather than parsed.
	public static string? ParseLinkOrNull(string? url)
	{
		if (string.IsNullOrWhiteSpace(url))
		{
			return null;
		}

		return NexusLink.TryParseId(url, out var resultId)
			? resultId
			: throw new NexusResultException("That's not a Nexus result link (https://nexus-shiyu.com/results/…).");
	}

	// A player is recognized by their Nexus nickname from this tournament's registration (the
	// "DarteNickname" field), or failing that their site name / Discord username — case- and
	// whitespace-insensitive. One recognized player is enough to place both; contradictory matches
	// (e.g. both players look like "left") leave the sides unknown.
	private async Task<bool?> MatchSidesAsync(Guid tournamentId, Guid p1Id, Guid p2Id, NexusParsedResult parsed, CancellationToken ct)
	{
		var p1Names = await GetKnownNamesAsync(tournamentId, p1Id, ct);
		var p2Names = await GetKnownNamesAsync(tournamentId, p2Id, ct);
		var left = NormalizeName(parsed.LeftName);
		var right = NormalizeName(parsed.RightName);

		var saysP1IsLeft = p1Names.Contains(left) || p2Names.Contains(right);
		var saysP1IsRight = p1Names.Contains(right) || p2Names.Contains(left);

		return saysP1IsLeft == saysP1IsRight ? null : saysP1IsLeft;
	}

	private async Task<HashSet<string>> GetKnownNamesAsync(Guid tournamentId, Guid playerId, CancellationToken ct)
	{
		var registration = await registrationRepository.FindExistingAsync(tournamentId, playerId, null, ct);
		var player = await playerRepository.GetEntityByIdAsync(playerId, ct);

		return new[] { registration?.Player1Details.DarteNickname, player?.Name, player?.DiscordUsername }
			.Where(name => !string.IsNullOrWhiteSpace(name))
			.Select(name => NormalizeName(name!))
			.ToHashSet();
	}

	private static string NormalizeName(string name) => name.Trim().ToLowerInvariant();
}
