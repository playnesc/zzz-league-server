using System.Globalization;
using System.Text.Json;
using ZenlessLeague.Api.Features.Tournaments;

namespace ZenlessLeague.Api.Features.Nexus;

public enum NexusSide { Left, Right }

public record NexusMissingValue(int Game, NexusSide Side);

// Left is the pasted draft's creator, Right its opponent (the creator can differ between the games
// of a series, so the other games' values are matched by Nexus userId). LeftTotal/RightTotal are
// already formatted like a manually entered result — integer score for Deadly Assault, "MM:SS" for
// Shiyu Defense — so MatchService's IsP1Win/ParseMinutes compare them unchanged. Null when no game
// is counted.
public record NexusParsedResult(
	string LeftName,
	string RightName,
	bool ScoreBased,
	int TotalGames,
	int CountedGames,
	long LeftSum,
	long RightSum,
	string? LeftTotal,
	string? RightTotal,
	List<NexusMissingValue> Missing)
{
	public bool IsTie => CountedGames > 0 && LeftSum == RightSum;
}

// Reads zzz.nexus's playnesc integration endpoint (/api/integrations/playnesc/drafts/{id}/results).
// One response = one draft (game) plus a `match` summary of its series (gameIds, totals, decided).
// The snapshot handed to Parse is a JSON array of draft responses: the pasted draft first, then the
// series' other drafts (NexusResultResolver fetches them), so unfinished games can be pointed out.
public static class NexusResultParser
{
	private const string Done = "DONE";

	// The series' draft ids other than the pasted one — empty when the draft has no series.
	public static List<string> ReadOtherGameIds(string draftJson)
	{
		try
		{
			using var document = JsonDocument.Parse(draftJson);
			var root = document.RootElement;
			if (root.ValueKind != JsonValueKind.Object ||
				!root.TryGetProperty("match", out var series) || series.ValueKind != JsonValueKind.Object ||
				!series.TryGetProperty("gameIds", out var gameIds) || gameIds.ValueKind != JsonValueKind.Array)
			{
				return [];
			}

			var ownId = root.GetProperty("id").GetString();
			return gameIds.EnumerateArray()
				.Select(id => id.GetString())
				.Where(id => !string.IsNullOrEmpty(id) && id != ownId)
				.Select(id => id!)
				.Distinct()
				.ToList();
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException)
		{
			throw new NexusFormatException("Couldn't read the Nexus result — Nexus may have changed its format.", ex);
		}
	}

	// Combines the pasted draft and its series' other drafts into the snapshot Parse reads.
	public static string BuildSnapshot(string draftJson, IEnumerable<string> otherDraftJsons) =>
		$"[{string.Join(",", new[] { draftJson }.Concat(otherDraftJsons).Select(json => json.Trim()))}]";

	// expectedMode is null when re-reading a stored snapshot (already validated at submission).
	public static NexusParsedResult Parse(string snapshotJson, TournamentGameMode? expectedMode)
	{
		try
		{
			using var document = JsonDocument.Parse(snapshotJson);
			var root = document.RootElement;
			var drafts = root.ValueKind == JsonValueKind.Array ? root.EnumerateArray().ToList() : [root];
			return Parse(drafts, expectedMode);
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
		{
			throw new NexusFormatException("Couldn't read the Nexus result — Nexus may have changed its format.", ex);
		}
	}

	private static NexusParsedResult Parse(List<JsonElement> drafts, TournamentGameMode? expectedMode)
	{
		// Nexus answered the old /api/results/{id} with 200 and a literal `null` for unknown ids.
		if (drafts.Count == 0 || drafts[0].ValueKind != JsonValueKind.Object)
		{
			throw new NexusResultException("Nexus result not found.");
		}

		var draft = drafts[0];
		var scoreBased = draft.GetProperty("unit").GetString() != "seconds";
		if (expectedMode is not null)
		{
			if (draft.TryGetProperty("mode", out var mode) && mode.GetString() is { } modeName && modeName != "SOLO")
			{
				throw new NexusResultException("This Nexus draft isn't a solo draft.");
			}

			if (scoreBased != (expectedMode == TournamentGameMode.DeadlyAssault))
			{
				throw new NexusResultException(scoreBased
					? "This Nexus result is a Deadly Assault result, but the tournament is Shiyu Defense."
					: "This Nexus result is a Shiyu Defense result, but the tournament is Deadly Assault.");
			}
		}

		var creator = draft.GetProperty("creator");
		var opponent = draft.GetProperty("opponent");
		var leftId = creator.GetProperty("userId").GetString();
		var rightId = opponent.GetProperty("userId").GetString();

		var series = draft.TryGetProperty("match", out var m) && m.ValueKind == JsonValueKind.Object ? m : (JsonElement?)null;
		var gameIds = series is { } s && s.TryGetProperty("gameIds", out var ids) && ids.ValueKind == JsonValueKind.Array
			? ids.EnumerateArray().Select(id => id.GetString()).ToList()
			: [draft.GetProperty("id").GetString()];
		var draftsById = drafts
			.Where(d => d.ValueKind == JsonValueKind.Object && d.TryGetProperty("id", out _))
			.GroupBy(d => d.GetProperty("id").GetString())
			.ToDictionary(g => g.Key ?? string.Empty, g => g.First());

		// Drafts of later games may not exist yet, so the series length comes from gameType where known.
		var totalGames = Math.Max(gameIds.Count, ExpectedGames(draft));

		var missing = new List<NexusMissingValue>();
		long leftSum = 0, rightSum = 0;
		var countedGames = 0;
		for (var game = 1; game <= totalGames; game++)
		{
			var gameDraft = game <= gameIds.Count && draftsById.TryGetValue(gameIds[game - 1] ?? string.Empty, out var d) ? d : (JsonElement?)null;
			var finished = gameDraft is { } g && g.TryGetProperty("status", out var status) && status.GetString() == Done;
			var left = finished ? ReadFinal(gameDraft!.Value, leftId) : null;
			var right = finished ? ReadFinal(gameDraft!.Value, rightId) : null;

			if (left is null)
			{
				missing.Add(new NexusMissingValue(game, NexusSide.Left));
			}

			if (right is null)
			{
				missing.Add(new NexusMissingValue(game, NexusSide.Right));
			}

			if (left is not null && right is not null)
			{
				leftSum += left.Value;
				rightSum += right.Value;
				countedGames++;
			}
		}

		// A decided series is taken as Nexus sums it up (its totals are authoritative).
		if (series is { } decidedSeries &&
			decidedSeries.TryGetProperty("decided", out var decided) && decided.ValueKind == JsonValueKind.True &&
			ReadTotal(decidedSeries, leftId) is long leftTotal && ReadTotal(decidedSeries, rightId) is long rightTotal)
		{
			leftSum = leftTotal;
			rightSum = rightTotal;
			countedGames = totalGames;
			missing.Clear();
		}

		return new NexusParsedResult(
			creator.GetProperty("nick").GetString() ?? string.Empty,
			opponent.GetProperty("nick").GetString() ?? string.Empty,
			scoreBased,
			totalGames,
			countedGames,
			leftSum,
			rightSum,
			countedGames > 0 ? Format(leftSum, scoreBased) : null,
			countedGames > 0 ? Format(rightSum, scoreBased) : null,
			missing);
	}

	private static int ExpectedGames(JsonElement draft) =>
		draft.TryGetProperty("gameType", out var gameType)
			? gameType.GetString() switch
			{
				"BEST_OF_ONE" => 1,
				"BEST_OF_TWO" => 2,
				_ => 0,
			}
			: 0;

	// The player's final (raw + restart penalty) in this draft, whichever of creator/opponent they are.
	private static long? ReadFinal(JsonElement draft, string? userId)
	{
		foreach (var role in new[] { "creator", "opponent" })
		{
			if (draft.TryGetProperty(role, out var player) && player.ValueKind == JsonValueKind.Object &&
				player.GetProperty("userId").GetString() == userId)
			{
				return Positive(player, "final");
			}
		}

		return null;
	}

	private static long? ReadTotal(JsonElement series, string? userId)
	{
		if (!series.TryGetProperty("totals", out var totals) || totals.ValueKind != JsonValueKind.Array)
		{
			return null;
		}

		var total = totals.EnumerateArray().FirstOrDefault(t => t.GetProperty("userId").GetString() == userId);
		return total.ValueKind == JsonValueKind.Object ? Positive(total, "final") : null;
	}

	// Zero/absent counts as missing — a real Shiyu clear can't take 0 seconds and a real Deadly
	// Assault score is never 0. Fractional values are rounded to the unit.
	private static long? Positive(JsonElement element, string property)
	{
		if (!element.TryGetProperty(property, out var value))
		{
			return null;
		}

		decimal? number = value.ValueKind switch
		{
			JsonValueKind.Number => value.GetDecimal(),
			JsonValueKind.String when decimal.TryParse(value.GetString(), NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) => parsed,
			JsonValueKind.Null => null,
			_ => throw new FormatException($"'{property}' is not a number."),
		};

		var rounded = number is { } n ? (long)Math.Round(n, MidpointRounding.AwayFromZero) : 0;
		return rounded > 0 ? rounded : null;
	}

	private static string Format(long value, bool scoreBased) =>
		scoreBased
			? value.ToString(CultureInfo.InvariantCulture)
			: $"{value / 60:00}:{value % 60:00}";
}
