using System.Text.Json;

namespace ZenlessLeague.Api.Tests;

// Real nexus-shiyu.com /api/results/{id} responses (Fixtures/Nexus, captured 2026-10-20; players
// "m_Debil" left vs "shad1w" right) plus a builder for edge cases those samples don't cover.
public static class NexusFixtures
{
	// scoreBased: g1 3 333 / 7 777, g2 77 777 / 33 333 → totals 81 110 / 41 110.
	public const string DeadlyAssaultId = "cmvfr4qka0ffh52vtem5hwxsf";

	// Times: g1 2:32 / 1:11, g2 2:22 / 2:32 → totals 4:54 / 3:43.
	public const string ShiyuFinishedId = "cmvfrhirw0i2652vtboz37ace";

	// Times: g1 1:23 / 2:22, g2 not played ("0:00" / "0:00").
	public const string ShiyuUnfinishedId = "cmvfrra1y0knv52vt7pl4zlnk";

	public static string Link(string id) => $"https://nexus-shiyu.com/results/{id}/obs";

	public static string DeadlyAssault => Load("nexus-deadly-assault.json");
	public static string ShiyuFinished => Load("nexus-shiyu-finished.json");
	public static string ShiyuUnfinished => Load("nexus-shiyu-unfinished.json");

	public static FakeNexusClient ClientWithSamples()
	{
		var client = new FakeNexusClient();
		client.Results[DeadlyAssaultId] = DeadlyAssault;
		client.Results[ShiyuFinishedId] = ShiyuFinished;
		client.Results[ShiyuUnfinishedId] = ShiyuUnfinished;
		return client;
	}

	public static string Build(string leftName, string rightName, bool scoreBased, params (string Left, string Right)[] games)
	{
		var timers = new Dictionary<string, object>();
		for (var i = 0; i < games.Length; i++)
		{
			timers[$"g{i + 1}"] = new { left = games[i].Left, right = games[i].Right, diff = "0" };
		}

		timers["total"] = new { left = "999", right = "999", diff = "0" };
		return JsonSerializer.Serialize(new { leftName, rightName, scoreBased, singleGame = false, currentGame = games.Length, timers });
	}

	private static string Load(string fileName) =>
		File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Nexus", fileName));
}
