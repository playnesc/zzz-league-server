using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using ZenlessLeague.Api.Features.Tournaments;

namespace ZenlessLeague.Api.Features.Nexus;

public enum NexusSide { Left, Right }

public record NexusMissingValue(int Game, NexusSide Side);

// LeftTotal/RightTotal are summed over CountedGames only (games with both values present) and are
// already formatted like a manually entered result — integer score for Deadly Assault, "MM:SS"
// for Shiyu Defense — so MatchService's IsP1Win/ParseMinutes compare them unchanged. Null when no
// game is counted.
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

public static partial class NexusResultParser
{
	[GeneratedRegex(@"^g(\d+)$")]
	private static partial Regex GameKeyRegex();

	// expectedMode is null when re-reading a stored snapshot (already validated at submission).
	public static NexusParsedResult Parse(string json, TournamentGameMode? expectedMode)
	{
		try
		{
			using var document = JsonDocument.Parse(json);
			return Parse(document.RootElement, expectedMode);
		}
		catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or FormatException)
		{
			throw new NexusFormatException("Couldn't read the Nexus result — Nexus may have changed its format.", ex);
		}
	}

	private static NexusParsedResult Parse(JsonElement root, TournamentGameMode? expectedMode)
	{
		// Nexus answers an unknown result id with 200 and a literal `null` body, not a 404.
		if (root.ValueKind != JsonValueKind.Object)
		{
			throw new NexusResultException("Nexus result not found.");
		}

		var scoreBased = root.GetProperty("scoreBased").GetBoolean();
		if (expectedMode is not null && scoreBased != (expectedMode == TournamentGameMode.DeadlyAssault))
		{
			throw new NexusResultException(scoreBased
				? "This Nexus result is a Deadly Assault result, but the tournament is Shiyu Defense."
				: "This Nexus result is a Shiyu Defense result, but the tournament is Deadly Assault.");
		}

		var singleGame = root.TryGetProperty("singleGame", out var singleGameElement) && singleGameElement.ValueKind == JsonValueKind.True;

		var games = root.GetProperty("timers").EnumerateObject()
			.Select(p => (Match: GameKeyRegex().Match(p.Name), p.Value))
			.Where(g => g.Match.Success)
			.Select(g => (Index: int.Parse(g.Match.Groups[1].Value, CultureInfo.InvariantCulture), g.Value))
			.Where(g => !singleGame || g.Index == 1)
			.OrderBy(g => g.Index)
			.ToList();

		var missing = new List<NexusMissingValue>();
		long leftSum = 0, rightSum = 0;
		var countedGames = 0;

		foreach (var (index, timer) in games)
		{
			var left = ReadValue(timer, "left", scoreBased);
			var right = ReadValue(timer, "right", scoreBased);

			if (left is null)
			{
				missing.Add(new NexusMissingValue(index, NexusSide.Left));
			}

			if (right is null)
			{
				missing.Add(new NexusMissingValue(index, NexusSide.Right));
			}

			if (left is not null && right is not null)
			{
				leftSum += left.Value;
				rightSum += right.Value;
				countedGames++;
			}
		}

		return new NexusParsedResult(
			root.GetProperty("leftName").GetString() ?? string.Empty,
			root.GetProperty("rightName").GetString() ?? string.Empty,
			scoreBased,
			games.Count,
			countedGames,
			leftSum,
			rightSum,
			countedGames > 0 ? Format(leftSum, scoreBased) : null,
			countedGames > 0 ? Format(rightSum, scoreBased) : null,
			missing);
	}

	// Nexus shows unplayed values as "0:00" / "0", so zero counts as missing too — a real Shiyu
	// clear can't take 0 seconds and a real Deadly Assault score is never 0.
	private static long? ReadValue(JsonElement timer, string side, bool scoreBased)
	{
		if (!timer.TryGetProperty(side, out var element) || element.ValueKind != JsonValueKind.String)
		{
			return null;
		}

		var value = scoreBased ? ParseScore(element.GetString()!) : ParseSeconds(element.GetString()!);
		return value is > 0 ? value : null;
	}

	// "81 110" — Nexus groups thousands with (possibly non-breaking) spaces.
	private static long? ParseScore(string text)
	{
		var digits = new string(text.Where(c => !char.IsWhiteSpace(c)).ToArray());
		return long.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var score) ? score : null;
	}

	// "4:54" (M:SS) or "1:04:54" (H:MM:SS).
	private static long? ParseSeconds(string text)
	{
		long seconds = 0;
		foreach (var part in text.Trim().Split(':'))
		{
			if (!long.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number))
			{
				return null;
			}

			seconds = seconds * 60 + number;
		}

		return seconds;
	}

	private static string Format(long value, bool scoreBased) =>
		scoreBased
			? value.ToString(CultureInfo.InvariantCulture)
			: $"{value / 60:00}:{value % 60:00}";
}
