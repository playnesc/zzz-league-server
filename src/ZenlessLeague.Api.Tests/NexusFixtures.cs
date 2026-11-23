using System.Text.Json;

namespace ZenlessLeague.Api.Tests;

// zzz.nexus playnesc-integration draft responses. Sample = the example response Nexus gave us
// (Fixtures/Nexus/nexus-draft-sample.json); the series below are built in that shape, players
// "m_Debil" (creator = left) vs "shad1w" (opponent = right). Each series is registered under its
// game 1 draft id (the one the tests paste); later games get "<id>g<N>".
public static class NexusFixtures
{
	// Deadly Assault, decided: g1 3 333 / 7 777, g2 77 777 / 33 333 → totals 81 110 / 41 110.
	public const string DeadlyAssaultId = "cmvfr4qka0ffh52vtem5hwxsf";

	// Shiyu, decided: g1 2:32 / 1:11, g2 2:22 / 2:32 → totals 4:54 / 3:43.
	public const string ShiyuFinishedId = "cmvfrhirw0i2652vtboz37ace";

	// Shiyu BO2, only g1 played (1:23 / 2:22); game 2's draft doesn't exist yet.
	public const string ShiyuUnfinishedId = "cmvfrra1y0knv52vt7pl4zlnk";

	public static string Link(string id) => $"https://zzz.nexus/drafts/{id}";

	public static string Sample => Load("nexus-draft-sample.json");

	// Real response, fetched from our server on 2026-11-23: Shiyu BO2, game 1 of 2, m_Debil
	// (creator) 125 s vs Ratper 97 s; decided, totals 252 / 209 s.
	public const string RealId = "cmwto087500ahx8vtbtg220hp";
	public const string RealGame2Id = "cmwtop85j000hlbvtpumrrc6r";

	public static string Real => Load("nexus-draft-real.json");

	// Real Deadly Assault response ("unit":"points", "discipline":"ASSAULT"): game 1 of 2, m_Debil
	// 3 333 vs SHIZO 7 777; decided, totals 81 110 / 41 110.
	public static string RealAssault => Load("nexus-draft-real-assault.json");

	// Real Shiyu response, game 1 of an undecided BO2 (m_Debil 83 s vs SHIZO 142 s); its totals'
	// finals are null. The paste was cut off inside `totals`, so the tail after it is reconstructed.
	public static string RealUndecided => Load("nexus-draft-real-undecided.json");
	public const string RealUndecidedGame2Id = "cmvfrs0se0ko152vt8twjvfr5";

	private static string Load(string fileName) =>
		File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Nexus", fileName));

	public static FakeNexusClient ClientWithSamples()
	{
		var client = new FakeNexusClient();
		AddSeries(client, DeadlyAssaultId, "m_Debil", "shad1w", scoreBased: true, decided: true, (3333, 7777), (77777, 33333));
		AddSeries(client, ShiyuFinishedId, "m_Debil", "shad1w", scoreBased: false, decided: true, (152, 71), (142, 152));
		AddSeries(client, ShiyuUnfinishedId, "m_Debil", "shad1w", scoreBased: false, decided: false, (83, 142));
		return client;
	}

	// One BO2 series (more games if given). A game with a null value isn't finished ("PLAYING").
	// Totals are the sums of finished games, as Nexus reports them.
	public static void AddSeries(
		FakeNexusClient client, string firstId, string leftNick, string rightNick, bool scoreBased, bool decided,
		params (long? Left, long? Right)[] games)
	{
		var gameIds = games.Select((_, i) => i == 0 ? firstId : $"{firstId}g{i + 1}").ToList();
		var done = games.Where(g => g.Left is not null && g.Right is not null).ToList();
		var series = new
		{
			gameIds,
			totals = new[]
			{
				new { userId = Uid(leftNick), nick = leftNick, final = done.Sum(g => g.Left!.Value) },
				new { userId = Uid(rightNick), nick = rightNick, final = done.Sum(g => g.Right!.Value) },
			},
			decided,
			winnerId = (string?)null,
		};

		for (var i = 0; i < games.Length; i++)
		{
			client.Results[gameIds[i]] = Draft(gameIds[i], i + 1, scoreBased, leftNick, games[i].Left, rightNick, games[i].Right, series);
		}
	}

	public static string Draft(
		string id, int gameNumber, bool scoreBased, string creatorNick, long? creatorFinal, string opponentNick, long? opponentFinal, object series) =>
		JsonSerializer.Serialize(new
		{
			id,
			groupId = "group-" + id,
			gameNumber,
			gameType = "BEST_OF_TWO",
			status = creatorFinal is not null && opponentFinal is not null ? "DONE" : "PLAYING",
			discipline = scoreBased ? "DEADLY_ASSAULT" : "SHIYU",
			mode = "SOLO",
			unit = scoreBased ? "points" : "seconds",
			creator = Player(creatorNick, creatorFinal),
			opponent = Player(opponentNick, opponentFinal),
			match = series,
		});

	private static object Player(string nick, long? final) =>
		new { userId = Uid(nick), nick, teamId = (string?)null, raw = final ?? 0, restartPenalty = 0, final = final ?? 0 };

	private static string Uid(string nick) => "user-" + nick;
}
