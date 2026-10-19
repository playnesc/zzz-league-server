using ZenlessLeague.Api.Features.Nexus;

namespace ZenlessLeague.Api.Tests;

// Hand-rolled test double — serves canned /api/results/{id} JSON by result id, so MatchService's
// Nexus flow can be tested without calling nexus-shiyu.com.
public class FakeNexusClient : INexusClient
{
	public Dictionary<string, string> Results { get; } = [];

	public Task<string> GetResultJsonAsync(string resultId, CancellationToken ct = default) =>
		Results.TryGetValue(resultId, out var json)
			? Task.FromResult(json)
			: throw new NexusResultException("Nexus result not found.");
}
