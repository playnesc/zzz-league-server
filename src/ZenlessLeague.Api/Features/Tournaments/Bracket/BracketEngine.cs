using ZenlessLeague.Api.Infrastructure.Errors;

namespace ZenlessLeague.Api.Features.Tournaments.Bracket;

public enum BracketFormat { SingleElimination, DoubleElimination }

public enum BracketSection { Winners, Losers, GrandFinal, ThirdPlace }

/// <summary>Thrown for an invalid bracket layout or an unsupported bracket option — maps to 400.</summary>
public class BracketValidationException(string message) : ValidationApiException(message);

public static class BracketFormats
{
	// Tournament.BracketType keeps the strings it has always stored (they were Challonge's
	// tournament_type values), so existing rows and the frontend's form options stay valid.
	public const string SingleElimination = "single elimination";
	public const string DoubleElimination = "double elimination";

	public static BracketFormat Parse(string? bracketType) => bracketType switch
	{
		null or SingleElimination => BracketFormat.SingleElimination,
		DoubleElimination => BracketFormat.DoubleElimination,
		_ => throw new BracketValidationException($"Unsupported bracket type '{bracketType}'."),
	};
}

// One generated match. Number is the play order (1-based) and doubles as the match's key within
// the bracket: WinnerTo/LoserTo point at another spec's Number, Slot 1 = P1, 2 = P2. Round and
// Position are the match's place in the full power-of-two tree *before* byes were removed, so a
// renderer can lay the bracket out with gaps where bye matches would have been.
public record BracketMatchSpec(
	int Number,
	BracketSection Section,
	int Round,
	int Position,
	Guid? P1,
	Guid? P2,
	int? WinnerToNumber,
	int? WinnerToSlot,
	int? LoserToNumber,
	int? LoserToSlot);

// A finished match as ComputeRanks needs it. LoserAdvances is whether the loser had somewhere to
// go (losers bracket, 3rd-place match, grand final) — a loser that didn't was eliminated there.
public record CompletedBracketMatch(BracketSection Section, int Round, Guid Winner, Guid Loser, bool LoserAdvances);

// Pure bracket generation and ranking — no I/O, so it's tested exhaustively on its own (see
// BracketEngineTests). BracketService maps specs to TournamentMatch rows.
public static class BracketEngine
{
	public static int SizeFor(int entrantCount)
	{
		if (entrantCount < 2)
		{
			throw new BracketValidationException("A bracket needs at least 2 participants.");
		}

		var size = 2;
		while (size < entrantCount)
		{
			size *= 2;
		}

		return size;
	}

	// `slots` is the round-1 layout: slot 2i plays slot 2i+1, null is a bye. Byes never become
	// matches — an entrant facing a bye is placed straight into the match it would have advanced to,
	// in the losers bracket too (see ResolveByes).
	public static List<BracketMatchSpec> Generate(BracketFormat format, IReadOnlyList<Guid?> slots, bool thirdPlace)
	{
		if (slots.Count < 2 || (slots.Count & (slots.Count - 1)) != 0)
		{
			throw new BracketValidationException("Bracket size must be a power of two.");
		}

		var nodes = new List<Node>();
		var winners = BuildWinners(slots, nodes);

		if (format == BracketFormat.SingleElimination)
		{
			if (thirdPlace && winners.Count >= 2)
			{
				var semis = winners[^2];
				nodes.Add(new Node(BracketSection.ThirdPlace, winners.Count, 0, new LoserOf(semis[0]), new LoserOf(semis[1])));
			}
		}
		else
		{
			BuildLosersAndGrandFinal(winners, nodes);
		}

		ResolveByes(nodes);
		return Emit(nodes);
	}

	// Standard competition ranking ("1, 2, 3, 3, 5"): an entrant's rank is 1 + the number of entrants
	// who went further. Champion and runner-up come from the last match (grand final / final); the
	// 3rd-place match splits 3rd and 4th; everyone else is ranked by the round they were knocked out
	// in — in double elimination that's always a losers-bracket round, since a winners-bracket loss
	// only drops you down.
	public static Dictionary<Guid, int> ComputeRanks(BracketFormat format, IReadOnlyList<CompletedBracketMatch> matches)
	{
		var finalMatch = format == BracketFormat.DoubleElimination
			? matches.Single(m => m.Section == BracketSection.GrandFinal)
			: matches.Where(m => m.Section == BracketSection.Winners).MaxBy(m => m.Round)!;

		// Larger = went further. Round numbers are small, so these constants sit above any of them.
		const int championKey = int.MaxValue;
		const int runnerUpKey = 1_000_000;
		const int thirdPlaceWinnerKey = 900_001;
		const int thirdPlaceLoserKey = 900_000;

		var keys = new Dictionary<Guid, int>();
		foreach (var match in matches)
		{
			if (ReferenceEquals(match, finalMatch))
			{
				keys[match.Winner] = championKey;
				keys[match.Loser] = runnerUpKey;
			}
			else if (match.Section == BracketSection.ThirdPlace)
			{
				keys[match.Winner] = thirdPlaceWinnerKey;
				keys[match.Loser] = thirdPlaceLoserKey;
			}
			else if (!match.LoserAdvances)
			{
				keys[match.Loser] = match.Round;
			}
		}

		return keys.ToDictionary(k => k.Key, k => 1 + keys.Values.Count(v => v > k.Value));
	}

	private static List<List<Node>> BuildWinners(IReadOnlyList<Guid?> slots, List<Node> nodes)
	{
		var rounds = new List<List<Node>>();
		var first = new List<Node>();
		for (var i = 0; i < slots.Count / 2; i++)
		{
			first.Add(new Node(BracketSection.Winners, 1, i, SlotSource(slots[2 * i]), SlotSource(slots[2 * i + 1])));
		}

		rounds.Add(first);
		while (rounds[^1].Count > 1)
		{
			var previous = rounds[^1];
			var next = new List<Node>();
			for (var i = 0; i < previous.Count / 2; i++)
			{
				next.Add(new Node(BracketSection.Winners, rounds.Count + 1, i, new WinnerOf(previous[2 * i]), new WinnerOf(previous[2 * i + 1])));
			}

			rounds.Add(next);
		}

		nodes.AddRange(rounds.SelectMany(r => r));
		return rounds;
	}

	// For k winners rounds the losers bracket has 2(k-1) rounds: round 1 pairs the winners-round-1
	// losers; each even round 2j takes the survivors plus the losers dropping from winners round j+1;
	// each odd round after that halves the field. Drop-ins are reversed on alternate rounds so two
	// players who just met in the winners bracket don't immediately meet again.
	private static void BuildLosersAndGrandFinal(List<List<Node>> winners, List<Node> nodes)
	{
		var k = winners.Count;
		var winnersFinal = winners[^1][0];

		if (k == 1)
		{
			// Two entrants: the loser of the only winners match gets their second life in the grand final.
			nodes.Add(new Node(BracketSection.GrandFinal, 1, 0, new WinnerOf(winnersFinal), new LoserOf(winnersFinal)));
			return;
		}

		var losers = new List<Node>();
		var current = new List<Node>();
		var firstWinners = winners[0];
		for (var i = 0; i < firstWinners.Count / 2; i++)
		{
			current.Add(new Node(BracketSection.Losers, 1, i, new LoserOf(firstWinners[2 * i]), new LoserOf(firstWinners[2 * i + 1])));
		}

		losers.AddRange(current);
		var round = 1;

		for (var j = 1; j < k; j++)
		{
			var dropping = winners[j];
			var reversed = j % 2 == 1;
			var dropRound = new List<Node>();
			round++;
			for (var i = 0; i < current.Count; i++)
			{
				var dropper = dropping[reversed ? dropping.Count - 1 - i : i];
				dropRound.Add(new Node(BracketSection.Losers, round, i, new WinnerOf(current[i]), new LoserOf(dropper)));
			}

			losers.AddRange(dropRound);
			current = dropRound;

			if (j < k - 1)
			{
				var halving = new List<Node>();
				round++;
				for (var i = 0; i < current.Count / 2; i++)
				{
					halving.Add(new Node(BracketSection.Losers, round, i, new WinnerOf(current[2 * i]), new WinnerOf(current[2 * i + 1])));
				}

				losers.AddRange(halving);
				current = halving;
			}
		}

		nodes.AddRange(losers);
		// Single grand final, no bracket reset — the same setup the league ran on Challonge
		// (grand_finals_modifier "single match").
		nodes.Add(new Node(BracketSection.GrandFinal, 1, 0, new WinnerOf(winnersFinal), new WinnerOf(current[0])));
	}

	// A match with a bye on either side is never played: it's removed, and every reference to its
	// winner now points at the other side directly (its "loser" becomes a bye). Repeated until no
	// match has a bye left — that cascades through the losers bracket, where a winners-round-1 bye
	// leaves a losers-round-1 match without an opponent.
	private static void ResolveByes(List<Node> nodes)
	{
		bool changed;
		do
		{
			changed = false;
			foreach (var node in nodes)
			{
				if (node.Removed || (node.A is not ByeSource && node.B is not ByeSource))
				{
					continue;
				}

				var winner = node.A is ByeSource ? node.B : node.A;
				node.Removed = true;
				foreach (var other in nodes)
				{
					if (!other.Removed)
					{
						other.A = Replace(other.A, node, winner);
						other.B = Replace(other.B, node, winner);
					}
				}

				changed = true;
			}
		}
		while (changed);
	}

	private static Source Replace(Source source, Node removed, Source winner) => source switch
	{
		WinnerOf w when w.Match == removed => winner,
		LoserOf l when l.Match == removed => ByeSource.Instance,
		_ => source,
	};

	private static List<BracketMatchSpec> Emit(List<Node> nodes)
	{
		var live = nodes
			.Where(n => !n.Removed)
			.OrderBy(PlayOrder)
			.ThenBy(n => n.Position)
			.ToList();

		for (var i = 0; i < live.Count; i++)
		{
			live[i].Number = i + 1;
		}

		return live.Select(n =>
		{
			var (winnerTo, winnerSlot) = FindTarget(live, s => s is WinnerOf w && w.Match == n);
			var (loserTo, loserSlot) = FindTarget(live, s => s is LoserOf l && l.Match == n);
			return new BracketMatchSpec(
				n.Number, n.Section, n.Round, n.Position,
				(n.A as EntrantSource)?.Id, (n.B as EntrantSource)?.Id,
				winnerTo, winnerSlot, loserTo, loserSlot);
		}).ToList();
	}

	// Interleaves the two brackets the way they're actually played: winners round r, then the losers
	// round(s) it feeds — W1, L1, W2, L2, W3, L3, L4… — so a match's number is always higher than
	// every match it depends on. The 3rd-place match goes right before the final, the grand final last.
	private static (int Stage, int Sub) PlayOrder(Node n) => n.Section switch
	{
		BracketSection.Winners => (2 * (n.Round - 1), 1),
		BracketSection.Losers => (2 * n.Round - 1, 0),
		BracketSection.ThirdPlace => (2 * (n.Round - 1), 0),
		_ => (int.MaxValue, 0),
	};

	private static (int? Number, int? Slot) FindTarget(List<Node> live, Func<Source, bool> feeds)
	{
		foreach (var node in live)
		{
			if (feeds(node.A))
			{
				return (node.Number, 1);
			}

			if (feeds(node.B))
			{
				return (node.Number, 2);
			}
		}

		return (null, null);
	}

	private static Source SlotSource(Guid? entrant) => entrant is Guid id ? new EntrantSource(id) : ByeSource.Instance;

	private abstract record Source;
	private sealed record EntrantSource(Guid Id) : Source;
	private sealed record ByeSource : Source
	{
		public static readonly ByeSource Instance = new();
	}
	private sealed record WinnerOf(Node Match) : Source;
	private sealed record LoserOf(Node Match) : Source;

	private sealed class Node(BracketSection section, int round, int position, Source a, Source b)
	{
		public BracketSection Section { get; } = section;
		public int Round { get; } = round;
		public int Position { get; } = position;
		public Source A { get; set; } = a;
		public Source B { get; set; } = b;
		public bool Removed { get; set; }
		public int Number { get; set; }
	}
}
