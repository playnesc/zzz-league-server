using ZenlessLeague.Api.Features.Tournaments.Bracket;

namespace ZenlessLeague.Api.Tests;

public class BracketEngineTests
{
	public static TheoryData<BracketFormat, bool> Formats => new()
	{
		{ BracketFormat.SingleElimination, false },
		{ BracketFormat.SingleElimination, true },
		{ BracketFormat.DoubleElimination, false },
	};

	[Theory]
	[MemberData(nameof(Formats))]
	public void EverySize_RandomLayoutsAndResults_PlayOutToOneChampion(BracketFormat format, bool thirdPlace)
	{
		var rng = new Random(1234);
		for (var n = 2; n <= 64; n++)
		{
			for (var run = 0; run < 5; run++)
			{
				var entrants = Entrants(n);
				var slots = BracketLayout.Random(entrants, rng);
				BracketLayout.Validate(slots, entrants);

				var specs = BracketEngine.Generate(format, slots, thirdPlace);

				var expectedCount = format == BracketFormat.DoubleElimination
					? 2 * n - 2
					: n - 1 + (thirdPlace && n >= 4 ? 1 : 0);
				Assert.Equal(expectedCount, specs.Count);
				Assert.Equal(Enumerable.Range(1, specs.Count), specs.Select(s => s.Number));

				// Play order is topological: a match only ever feeds a later one.
				Assert.All(specs, s =>
				{
					Assert.True(s.WinnerToNumber is null || s.WinnerToNumber > s.Number);
					Assert.True(s.LoserToNumber is null || s.LoserToNumber > s.Number);
				});

				var completed = Play(specs, rng, out var played);
				Assert.True(entrants.All(played.Contains), $"n={n}: an entrant never played.");

				var ranks = BracketEngine.ComputeRanks(format, completed);
				Assert.Equal(n, ranks.Count);
				Assert.Single(ranks, r => r.Value == 1);
				Assert.Single(ranks, r => r.Value == 2);
			}
		}
	}

	[Fact]
	public void DoubleElimination_Eight_MatchesTheExpectedShape()
	{
		var e = Entrants(8);
		var specs = BracketEngine.Generate(BracketFormat.DoubleElimination, e.Cast<Guid?>().ToList(), thirdPlace: false);

		Assert.Equal(
			[
				BracketSection.Winners, BracketSection.Winners, BracketSection.Winners, BracketSection.Winners,
				BracketSection.Losers, BracketSection.Losers,
				BracketSection.Winners, BracketSection.Winners,
				BracketSection.Losers, BracketSection.Losers,
				BracketSection.Winners,
				BracketSection.Losers, BracketSection.Losers,
				BracketSection.GrandFinal,
			],
			specs.Select(s => s.Section));

		var m = specs.ToDictionary(s => s.Number);
		Assert.Equal((7, 1), (m[1].WinnerToNumber, m[1].WinnerToSlot));
		Assert.Equal((5, 1), (m[1].LoserToNumber, m[1].LoserToSlot));
		// Winners round 2 drops into losers round 2 reversed: M7's loser meets M6's winner (M10),
		// M8's loser meets M5's winner (M9) — never someone they could have just played.
		Assert.Equal((10, 2), (m[7].LoserToNumber, m[7].LoserToSlot));
		Assert.Equal((9, 2), (m[8].LoserToNumber, m[8].LoserToSlot));
		Assert.Equal((13, 2), (m[11].LoserToNumber, m[11].LoserToSlot));
		Assert.Equal((14, 1), (m[11].WinnerToNumber, m[11].WinnerToSlot));
		Assert.Equal((14, 2), (m[13].WinnerToNumber, m[13].WinnerToSlot));
		Assert.Null(m[14].WinnerToNumber);
		Assert.Null(m[14].LoserToNumber);
	}

	[Fact]
	public void DoubleElimination_Two_LoserGetsASecondChanceInTheGrandFinal()
	{
		var e = Entrants(2);
		var specs = BracketEngine.Generate(BracketFormat.DoubleElimination, e.Cast<Guid?>().ToList(), thirdPlace: false);

		Assert.Equal(2, specs.Count);
		Assert.Equal((2, 1), (specs[0].WinnerToNumber, specs[0].WinnerToSlot));
		Assert.Equal((2, 2), (specs[0].LoserToNumber, specs[0].LoserToSlot));
		Assert.Equal(BracketSection.GrandFinal, specs[1].Section);
	}

	[Fact]
	public void Byes_EntrantGoesStraightToTheNextMatch()
	{
		var e = Entrants(3);
		// a vs bye, b vs c
		var specs = BracketEngine.Generate(BracketFormat.SingleElimination, [e[0], null, e[1], e[2]], thirdPlace: true);

		// No 3rd-place match: only one semifinal is actually played.
		Assert.Equal(2, specs.Count);
		Assert.Equal((e[1], e[2]), (specs[0].P1, specs[0].P2));
		Assert.Equal(e[0], specs[1].P1);
		Assert.Null(specs[1].P2);
		Assert.Equal((2, 2), (specs[0].WinnerToNumber, specs[0].WinnerToSlot));
	}

	[Fact]
	public void Byes_RemovedFromTheLosersBracketToo()
	{
		var e = Entrants(3);
		var specs = BracketEngine.Generate(BracketFormat.DoubleElimination, [e[0], e[1], e[2], null], thirdPlace: false);

		Assert.Equal(4, specs.Count);
		var m = specs.ToDictionary(s => s.Number);
		// M1 a-b's loser skips the (bye) losers round 1 and lands in the losers final.
		Assert.Equal(BracketSection.Losers, m[m[1].LoserToNumber!.Value].Section);
		Assert.All(specs, s => Assert.False(s.Section == BracketSection.Losers && s.Round == 1));
	}

	[Fact]
	public void Ranks_SingleElimination_SemifinalLosersShareThird()
	{
		var ranks = PlayFavorites(BracketFormat.SingleElimination, thirdPlace: false);
		Assert.Equal([1, 2, 3, 3, 5, 5, 5, 5], ranks);
	}

	[Fact]
	public void Ranks_SingleElimination_ThirdPlaceMatchSplitsThirdAndFourth()
	{
		var ranks = PlayFavorites(BracketFormat.SingleElimination, thirdPlace: true);
		Assert.Equal([1, 2, 3, 4, 5, 5, 5, 5], ranks);
	}

	[Fact]
	public void Ranks_DoubleElimination_ByLosersRoundKnockedOutIn()
	{
		var ranks = PlayFavorites(BracketFormat.DoubleElimination, thirdPlace: false);
		Assert.Equal([1, 2, 3, 4, 5, 5, 7, 7], ranks);
	}

	[Fact]
	public void Validate_RejectsWrongSize()
	{
		var e = Entrants(3);
		Assert.Throws<BracketValidationException>(() => BracketLayout.Validate([e[0], e[1], e[2], null, null, null, null, null], e));
	}

	[Fact]
	public void Validate_RejectsMissingOrDuplicatedEntrant()
	{
		var e = Entrants(3);
		Assert.Throws<BracketValidationException>(() => BracketLayout.Validate([e[0], e[0], e[2], null], e));
		Assert.Throws<BracketValidationException>(() => BracketLayout.Validate([e[0], Guid.NewGuid(), e[2], null], e));
	}

	[Fact]
	public void Validate_RejectsTwoByesInOnePair()
	{
		var e = Entrants(5);
		Assert.Throws<BracketValidationException>(() => BracketLayout.Validate([e[0], e[1], e[2], e[3], e[4], null, null, null], e));
		BracketLayout.Validate([e[0], null, e[1], null, e[2], null, e[3], e[4]], e);
	}

	[Fact]
	public void Parse_UnknownBracketType_Throws()
	{
		Assert.Equal(BracketFormat.SingleElimination, BracketFormats.Parse(null));
		Assert.Equal(BracketFormat.DoubleElimination, BracketFormats.Parse("double elimination"));
		Assert.Throws<BracketValidationException>(() => BracketFormats.Parse("round robin"));
	}

	private static List<Guid> Entrants(int n) => Enumerable.Range(0, n).Select(_ => Guid.NewGuid()).ToList();

	// Eight entrants in slot order where the lower index always wins — returns the ranks handed out,
	// best to worst.
	private static int[] PlayFavorites(BracketFormat format, bool thirdPlace)
	{
		var e = Entrants(8);
		var strength = e.Select((id, i) => (id, i)).ToDictionary(x => x.id, x => x.i);
		var specs = BracketEngine.Generate(format, e.Cast<Guid?>().ToList(), thirdPlace);
		var completed = Play(specs, (p1, p2) => strength[p1] < strength[p2], out _);
		var ranks = BracketEngine.ComputeRanks(format, completed);
		return e.Select(id => ranks[id]).Order().ToArray();
	}

	private static List<CompletedBracketMatch> Play(List<BracketMatchSpec> specs, Random rng, out HashSet<Guid> played) =>
		Play(specs, (_, _) => rng.Next(2) == 0, out played);

	// Plays the matches in number order, moving entrants along WinnerTo/LoserTo exactly as
	// BracketService does — a match reached with a side still empty means the bracket is broken.
	private static List<CompletedBracketMatch> Play(List<BracketMatchSpec> specs, Func<Guid, Guid, bool> p1Wins, out HashSet<Guid> played)
	{
		var sides = specs.ToDictionary(s => s.Number, s => new Guid?[] { s.P1, s.P2 });
		var completed = new List<CompletedBracketMatch>();
		played = [];

		foreach (var spec in specs)
		{
			var p1 = sides[spec.Number][0] ?? throw new Xunit.Sdk.XunitException($"M{spec.Number} has no P1 when it's due.");
			var p2 = sides[spec.Number][1] ?? throw new Xunit.Sdk.XunitException($"M{spec.Number} has no P2 when it's due.");
			played.Add(p1);
			played.Add(p2);

			var (winner, loser) = p1Wins(p1, p2) ? (p1, p2) : (p2, p1);
			if (spec.WinnerToNumber is int w)
			{
				sides[w][spec.WinnerToSlot!.Value - 1] = winner;
			}

			if (spec.LoserToNumber is int l)
			{
				sides[l][spec.LoserToSlot!.Value - 1] = loser;
			}

			completed.Add(new CompletedBracketMatch(spec.Section, spec.Round, winner, loser, spec.LoserToNumber is not null));
		}

		return completed;
	}
}
