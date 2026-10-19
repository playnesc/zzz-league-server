using ZenlessLeague.Api.Features.Nexus;
using ZenlessLeague.Api.Features.Tournaments;

namespace ZenlessLeague.Api.Tests;

public class NexusResultParserTests
{
	[Fact]
	public void Parse_DeadlyAssault_SumsScoresWithoutThousandsSeparators()
	{
		var result = NexusResultParser.Parse(NexusFixtures.DeadlyAssault, TournamentGameMode.DeadlyAssault);

		Assert.Equal("m_Debil", result.LeftName);
		Assert.Equal("shad1w", result.RightName);
		Assert.True(result.ScoreBased);
		Assert.Equal(2, result.TotalGames);
		Assert.Equal(2, result.CountedGames);
		Assert.Equal("81110", result.LeftTotal);
		Assert.Equal("41110", result.RightTotal);
		Assert.Empty(result.Missing);
		Assert.False(result.IsTie);
	}

	[Fact]
	public void Parse_ShiyuFinished_SumsTimesAsMinutesSeconds()
	{
		var result = NexusResultParser.Parse(NexusFixtures.ShiyuFinished, TournamentGameMode.ShiyuDefense);

		Assert.False(result.ScoreBased);
		Assert.Equal(2, result.CountedGames);
		Assert.Equal("04:54", result.LeftTotal);
		Assert.Equal("03:43", result.RightTotal);
		Assert.Empty(result.Missing);
	}

	[Fact]
	public void Parse_ShiyuUnfinished_CountsOnlyGameOneAndReportsMissingValues()
	{
		var result = NexusResultParser.Parse(NexusFixtures.ShiyuUnfinished, TournamentGameMode.ShiyuDefense);

		Assert.Equal(2, result.TotalGames);
		Assert.Equal(1, result.CountedGames);
		Assert.Equal("01:23", result.LeftTotal);
		Assert.Equal("02:22", result.RightTotal);
		Assert.Equal([new NexusMissingValue(2, NexusSide.Left), new NexusMissingValue(2, NexusSide.Right)], result.Missing);
	}

	[Fact]
	public void Parse_OneSidedGame_IsNotCountedForEitherPlayer()
	{
		var json = NexusFixtures.Build("a", "b", scoreBased: false, ("1:00", "2:00"), ("1:30", "0:00"));

		var result = NexusResultParser.Parse(json, TournamentGameMode.ShiyuDefense);

		Assert.Equal(1, result.CountedGames);
		Assert.Equal("01:00", result.LeftTotal);
		Assert.Equal("02:00", result.RightTotal);
		Assert.Equal([new NexusMissingValue(2, NexusSide.Right)], result.Missing);
	}

	[Fact]
	public void Parse_NoFinishedGame_HasNoTotals()
	{
		var json = NexusFixtures.Build("a", "b", scoreBased: true, ("0", "0"), ("0", "0"));

		var result = NexusResultParser.Parse(json, TournamentGameMode.DeadlyAssault);

		Assert.Equal(0, result.CountedGames);
		Assert.Null(result.LeftTotal);
		Assert.Null(result.RightTotal);
		Assert.Equal(4, result.Missing.Count);
	}

	[Fact]
	public void Parse_EqualTotals_IsTie()
	{
		var json = NexusFixtures.Build("a", "b", scoreBased: true, ("1 000", "2 000"), ("2 000", "1 000"));

		Assert.True(NexusResultParser.Parse(json, TournamentGameMode.DeadlyAssault).IsTie);
	}

	[Theory]
	[InlineData(TournamentGameMode.ShiyuDefense, "deadly")]
	[InlineData(TournamentGameMode.DeadlyAssault, "shiyu")]
	public void Parse_WrongGameMode_Throws(TournamentGameMode mode, string sample)
	{
		var json = sample == "deadly" ? NexusFixtures.DeadlyAssault : NexusFixtures.ShiyuFinished;

		Assert.Throws<NexusResultException>(() => NexusResultParser.Parse(json, mode));
	}

	[Fact]
	public void Parse_WithoutExpectedMode_SkipsModeCheck()
	{
		var result = NexusResultParser.Parse(NexusFixtures.DeadlyAssault, expectedMode: null);

		Assert.Equal("81110", result.LeftTotal);
	}

	[Fact]
	public void Parse_NullBody_IsNotFound()
	{
		// What nexus-shiyu.com actually returns (with 200) for an unknown result id.
		var ex = Assert.Throws<NexusResultException>(() => NexusResultParser.Parse("null", TournamentGameMode.ShiyuDefense));

		Assert.Equal("Nexus result not found.", ex.Message);
	}

	[Theory]
	[InlineData("not json")]
	[InlineData("{\"leftName\":\"a\"}")]
	[InlineData("{\"leftName\":\"a\",\"rightName\":\"b\",\"scoreBased\":\"yes\",\"timers\":{}}")]
	public void Parse_Malformed_ThrowsFormatException(string json)
	{
		// Unreadable shape = Nexus probably changed its format → 502 "report to the admins", not a user error.
		Assert.Throws<NexusFormatException>(() => NexusResultParser.Parse(json, TournamentGameMode.ShiyuDefense));
	}

	[Theory]
	[InlineData("https://nexus-shiyu.com/results/cmvfr4qka0ffh52vtem5hwxsf/obs", "cmvfr4qka0ffh52vtem5hwxsf")]
	[InlineData("https://nexus-shiyu.com/results/cmvfr4qka0ffh52vtem5hwxsf", "cmvfr4qka0ffh52vtem5hwxsf")]
	[InlineData("  https://www.nexus-shiyu.com/results/CMVFR4QKA0FFH52VTEM5HWXSF/obs/  ", "cmvfr4qka0ffh52vtem5hwxsf")]
	[InlineData("https://nexus-shiyu.com/results/cmvfr4qka0ffh52vtem5hwxsf/obs?x=1", "cmvfr4qka0ffh52vtem5hwxsf")]
	public void NexusLink_AcceptsResultLinks(string url, string expectedId)
	{
		Assert.True(NexusLink.TryParseId(url, out var id));
		Assert.Equal(expectedId, id);
	}

	[Theory]
	[InlineData(null)]
	[InlineData("")]
	[InlineData("cmvfr4qka0ffh52vtem5hwxsf")]
	[InlineData("https://evil.example/results/cmvfr4qka0ffh52vtem5hwxsf/obs")]
	[InlineData("https://nexus-shiyu.com.evil.example/results/cmvfr4qka0ffh52vtem5hwxsf")]
	[InlineData("https://nexus-shiyu.com/api/results/cmvfr4qka0ffh52vtem5hwxsf")]
	[InlineData("https://nexus-shiyu.com/results/../admin")]
	public void NexusLink_RejectsAnythingElse(string? url)
	{
		Assert.False(NexusLink.TryParseId(url, out _));
	}
}
