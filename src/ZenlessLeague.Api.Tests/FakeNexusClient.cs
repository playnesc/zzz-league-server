using ZenlessLeague.Api.Features.Nexus;

namespace ZenlessLeague.Api.Tests;

// Hand-rolled test double — serves canned integration-endpoint draft JSON by draft id, so
// MatchService's Nexus flow can be tested without calling zzz.nexus.
public class FakeNexusClient : INexusClient
{
	public Dictionary<string, string> Results { get; } = [];

	public Task<string> GetDraftResultJsonAsync(string draftId, CancellationToken ct = default) =>
		Results.TryGetValue(draftId, out var json)
			? Task.FromResult(json)
			: throw new NexusResultException("Nexus result not found.");
}
