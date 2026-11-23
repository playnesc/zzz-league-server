using ZenlessLeague.Api.Features.Nexus;
using ZenlessLeague.Api.Features.Tournaments;

namespace ZenlessLeague.Api.Tests;

public class NexusResultParserTests
{
	private static NexusParsedResult ParseSeries(FakeNexusClient client, string firstId, TournamentGameMode? mode)
	{
		var pasted = client.Results[firstId];
		var others = NexusResultParser.ReadOtherGameIds(pasted).Select(id => client.Results[id]);
		return NexusResultParser.Parse(NexusResultParser.BuildSnapshot(pasted, others), mode);
	}

	[Fact]
	public void Parse_Sample_UsesDecidedSeriesTotalsWithRestartPenalties()
	{
		// Only game 2 is in the snapshot (game 1's draft wasn't fetched) — the decided series'
		// totals are used as Nexus sums them up anyway.
		var result = NexusResultParser.Parse(NexusResultParser.BuildSnapshot(NexusFixtures.Sample, []), TournamentGameMode.ShiyuDefense);

		Assert.Equal("Kitsune", result.LeftName); // creator
		Assert.Equal("Astra", result.RightName); // opponent
		Assert.False(result.ScoreBased);
		Assert.Equal(2, result.TotalGames);
		Assert.Equal(2, result.CountedGames);
		Assert.Equal("04:08", result.LeftTotal); // 248 s
		Assert.Equal("03:55", result.RightTotal); // 235 s
		Assert.Empty(result.Missing);
	}

	[Fact]
	public void Parse_RealResponse_DecidedSeries()
	{
		var result = NexusResultParser.Parse(NexusResultParser.BuildSnapshot(NexusFixtures.Real, []), TournamentGameMode.ShiyuDefense);

		Assert.Equal("m_Debil", result.LeftName);
		Assert.Equal("Ratper", result.RightName);
		Assert.Equal(2, result.CountedGames);
		Assert.Equal("04:12", result.LeftTotal); // 252 s
		Assert.Equal("03:29", result.RightTotal); // 209 s — Ratper wins, as Nexus's winnerId says
		Assert.Empty(result.Missing);
		Assert.Equal([NexusFixtures.RealGame2Id], NexusResultParser.ReadOtherGameIds(NexusFixtures.Real));
	}

	[Fact]
	public void Parse_RealAssaultResponse_IsScoreBasedAndUsesTotals()
	{
		var result = NexusResultParser.Parse(NexusResultParser.BuildSnapshot(NexusFixtures.RealAssault, []), TournamentGameMode.DeadlyAssault);

		Assert.True(result.ScoreBased);
		Assert.Equal("81110", result.LeftTotal); // m_Debil — Nexus's series winner
		Assert.Equal("41110", result.RightTotal);
		Assert.Throws<NexusResultException>(() =>
			NexusResultParser.Parse(NexusResultParser.BuildSnapshot(NexusFixtures.RealAssault, []), TournamentGameMode.ShiyuDefense));
	}

	[Fact]
	public void Parse_RealUndecidedResponse_NullTotals_CountsFinishedGame()
	{
		// Game 2's draft exists but isn't finished yet.
		var game2 = NexusFixtures.Draft(NexusFixtures.RealUndecidedGame2Id, 2, false, "SHIZO", null, "m_Debil", null, new { });

		var result = NexusResultParser.Parse(NexusResultParser.BuildSnapshot(NexusFixtures.RealUndecided, [game2]), TournamentGameMode.ShiyuDefense);

		Assert.Equal(2, result.TotalGames);
		Assert.Equal(1, result.CountedGames);
		Assert.Equal("01:23", result.LeftTotal);
		Assert.Equal("02:22", result.RightTotal);
		Assert.Equal([new NexusMissingValue(2, NexusSide.Left), new NexusMissingValue(2, NexusSide.Right)], result.Missing);
	}

	[Fact]
	public void Parse_RealResponse_AsUndecidedSeries_CountsOnlyTheFetchedGame()
	{
		var undecided = NexusFixtures.Real.Replace("\"decided\":true", "\"decided\":false");

		var result = NexusResultParser.Parse(NexusResultParser.BuildSnapshot(undecided, []), TournamentGameMode.ShiyuDefense);

		Assert.Equal(1, result.CountedGames);
		Assert.Equal("02:05", result.LeftTotal);
		Assert.Equal("01:37", result.RightTotal);
		Assert.Equal([new NexusMissingValue(2, NexusSide.Left), new NexusMissingValue(2, NexusSide.Right)], result.Missing);
	}

	[Fact]
	public void Parse_DeadlyAssault_SumsScores()
	{
		var result = ParseSeries(NexusFixtures.ClientWithSamples(), NexusFixtures.DeadlyAssaultId, TournamentGameMode.DeadlyAssault);

		Assert.Equal("m_Debil", result.LeftName);
		Assert.Equal("shad1w", result.RightName);
		Assert.True(result.ScoreBased);
		Assert.Equal(2, result.CountedGames);
		Assert.Equal("81110", result.LeftTotal);
		Assert.Equal("41110", result.RightTotal);
		Assert.Empty(result.Missing);
		Assert.False(result.IsTie);
	}

	[Fact]
	public void Parse_ShiyuFinished_SumsTimesAsMinutesSeconds()
	{
		var result = ParseSeries(NexusFixtures.ClientWithSamples(), NexusFixtures.ShiyuFinishedId, TournamentGameMode.ShiyuDefense);

		Assert.Equal(2, result.CountedGames);
		Assert.Equal("04:54", result.LeftTotal);
		Assert.Equal("03:43", result.RightTotal);
	}

	[Fact]
	public void Parse_UndecidedSeries_GameTwoNotCreatedYet_CountsGameOneAndReportsMissing()
	{
		var result = ParseSeries(NexusFixtures.ClientWithSamples(), NexusFixtures.ShiyuUnfinishedId, TournamentGameMode.ShiyuDefense);

		Assert.Equal(2, result.TotalGames); // from gameType BEST_OF_TWO
		Assert.Equal(1, result.CountedGames);
		Assert.Equal("01:23", result.LeftTotal);
		Assert.Equal("02:22", result.RightTotal);
		Assert.Equal([new NexusMissingValue(2, NexusSide.Left), new NexusMissingValue(2, NexusSide.Right)], result.Missing);
	}

	[Fact]
	public void Parse_UndecidedSeries_PastingTheUnfinishedGame_StillCountsTheFinishedOne()
	{
		var client = new FakeNexusClient();
		NexusFixtures.AddSeries(client, "seriesgame1x", "a", "b", scoreBased: false, decided: false, (60, 90), (null, null));

		var result = ParseSeries(client, "seriesgame1xg2", TournamentGameMode.ShiyuDefense);

		Assert.Equal(1, result.CountedGames);
		Assert.Equal("01:00", result.LeftTotal);
		Assert.Equal([new NexusMissingValue(2, NexusSide.Left), new NexusMissingValue(2, NexusSide.Right)], result.Missing);
	}

	[Fact]
	public void Parse_CreatorSwapsBetweenGames_MatchesValuesByUserId()
	{
		var client = new FakeNexusClient();
		var series = new
		{
			gameIds = new[] { "swapgame0001", "swapgame0002" },
			totals = Array.Empty<object>(),
			decided = false,
		};
		client.Results["swapgame0001"] = NexusFixtures.Draft("swapgame0001", 1, false, "a", 100, "b", 200, series);
		client.Results["swapgame0002"] = NexusFixtures.Draft("swapgame0002", 2, false, "b", 50, "a", 10, series);

		var result = ParseSeries(client, "swapgame0001", TournamentGameMode.ShiyuDefense);

		Assert.Equal("a", result.LeftName);
		Assert.Equal(110, result.LeftSum);
		Assert.Equal(250, result.RightSum);
	}

	[Fact]
	public void Parse_NoFinishedGame_HasNoTotals()
	{
		var client = new FakeNexusClient();
		NexusFixtures.AddSeries(client, "nothingplayed", "a", "b", scoreBased: true, decided: false, (null, null), (null, null));

		var result = ParseSeries(client, "nothingplayed", TournamentGameMode.DeadlyAssault);

		Assert.Equal(0, result.CountedGames);
		Assert.Null(result.LeftTotal);
		Assert.Null(result.RightTotal);
		Assert.Equal(4, result.Missing.Count);
	}

	[Fact]
	public void Parse_EqualTotals_IsTie()
	{
		var client = new FakeNexusClient();
		NexusFixtures.AddSeries(client, "tiedseries00", "a", "b", scoreBased: true, decided: true, (1000, 2000), (2000, 1000));

		Assert.True(ParseSeries(client, "tiedseries00", TournamentGameMode.DeadlyAssault).IsTie);
	}

	[Theory]
	[InlineData(TournamentGameMode.ShiyuDefense, NexusFixtures.DeadlyAssaultId)]
	[InlineData(TournamentGameMode.DeadlyAssault, NexusFixtures.ShiyuFinishedId)]
	public void Parse_WrongGameMode_Throws(TournamentGameMode mode, string id)
	{
		Assert.Throws<NexusResultException>(() => ParseSeries(NexusFixtures.ClientWithSamples(), id, mode));
	}

	[Fact]
	public void Parse_TeamDraft_Throws()
	{
		var json = NexusFixtures.Sample.Replace("\"SOLO\"", "\"TEAM\"");

		Assert.Throws<NexusResultException>(() => NexusResultParser.Parse(json, TournamentGameMode.ShiyuDefense));
	}

	[Fact]
	public void Parse_WithoutExpectedMode_SkipsModeCheck()
	{
		var result = ParseSeries(NexusFixtures.ClientWithSamples(), NexusFixtures.DeadlyAssaultId, mode: null);

		Assert.Equal("81110", result.LeftTotal);
	}

	[Theory]
	[InlineData("null")]
	[InlineData("[null]")]
	public void Parse_NullBody_IsNotFound(string json)
	{
		var ex = Assert.Throws<NexusResultException>(() => NexusResultParser.Parse(json, TournamentGameMode.ShiyuDefense));

		Assert.Equal("Nexus result not found.", ex.Message);
	}

	[Theory]
	[InlineData("not json")]
	[InlineData("{\"id\":\"a\"}")]
	// A snapshot stored from the pre-integration /api/results/{id} endpoint.
	[InlineData("{\"leftName\":\"a\",\"rightName\":\"b\",\"scoreBased\":false,\"timers\":{}}")]
	[InlineData("{\"id\":\"a\",\"unit\":\"seconds\",\"creator\":{\"userId\":\"x\",\"nick\":\"x\",\"final\":\"soon\"},\"opponent\":{\"userId\":\"y\",\"nick\":\"y\",\"final\":1},\"status\":\"DONE\"}")]
	public void Parse_Malformed_ThrowsFormatException(string json)
	{
		// Unreadable shape = Nexus probably changed its format → 502 "report to the admins", not a user error.
		Assert.Throws<NexusFormatException>(() => NexusResultParser.Parse(json, TournamentGameMode.ShiyuDefense));
	}

	[Fact]
	public void ReadOtherGameIds_ReturnsSiblingsOnly()
	{
		Assert.Equal(["draft-101"], NexusResultParser.ReadOtherGameIds(NexusFixtures.Sample));
	}

	[Theory]
	[InlineData("https://zzz.nexus/drafts/cmwto087500ahx8vtbtg220hp", "cmwto087500ahx8vtbtg220hp")]
	[InlineData("https://zzz.nexus/results/cmwto087500ahx8vtbtg220hp/obs", "cmwto087500ahx8vtbtg220hp")]
	[InlineData("https://zzz.nexus/drafts/cmwto087500ahx8vtbtg220hp?DraftKey=5796b9be4b374d4e9f1b17dae7d8bf98", "cmwto087500ahx8vtbtg220hp")]
	[InlineData("https://zzz.nexus/drafts/cmwto087500ahx8vtbtg220hp?adminToken=cmwto087500ajx8vto2lbfkfy", "cmwto087500ahx8vtbtg220hp")]
	[InlineData("https://nexus-shiyu.com/results/cmvfr4qka0ffh52vtem5hwxsf", "cmvfr4qka0ffh52vtem5hwxsf")]
	[InlineData("  https://www.nexus-shiyu.com/results/CMVFR4QKA0FFH52VTEM5HWXSF/obs/  ", "cmvfr4qka0ffh52vtem5hwxsf")]
	[InlineData("https://nexus-shiyu.com/results/cmvfr4qka0ffh52vtem5hwxsf/obs?x=1", "cmvfr4qka0ffh52vtem5hwxsf")]
	[InlineData("https://www.zzz.nexus/results/cmvpibq63008f4zvttnujy2fa", "cmvpibq63008f4zvttnujy2fa")]
	public void NexusLink_AcceptsDraftAndResultLinks(string url, string expectedId)
	{
		Assert.True(NexusLink.TryParseId(url, out var id));
		Assert.Equal(expectedId, id);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("cmvfr4qka0ffh52vtem5hwxsf")]
	[InlineData("https://evil.example/results/cmvfr4qka0ffh52vtem5hwxsf/obs")]
	[InlineData("https://evil.example/drafts/cmvfr4qka0ffh52vtem5hwxsf")]
	[InlineData("https://nexus-shiyu.com.evil.example/results/cmvfr4qka0ffh52vtem5hwxsf")]
	[InlineData("https://zzz.nexus.evil.example/drafts/cmvpibq63008f4zvttnujy2fa")]
	[InlineData("https://zzzanexus/results/cmvpibq63008f4zvttnujy2fa")]
	[InlineData("https://zzz.nexus/api/integrations/playnesc/drafts/cmvfr4qka0ffh52vtem5hwxsf/results")]
	[InlineData("https://zzz.nexus/drafts/cmvfr4qka0ffh52vtem5hwxsf/obs/extra")]
	[InlineData("https://nexus-shiyu.com/results/../admin")]
	public void NexusLink_RejectsAnythingElse(string? url)
	{
		Assert.False(NexusLink.TryParseId(url, out _));
	}
}
