using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Google.Apis.Auth.OAuth2;

namespace ZenlessLeague.Migrator;

public interface IRtdbReader
{
	// Reads an RTDB object-map node (e.g. "players", "tournaments") as raw elements — callers
	// deserialize each value into the specific Legacy* type they expect. Returns null for an
	// empty/nonexistent path (RTDB's literal JSON "null" for a path with no data).
	Task<Dictionary<string, JsonElement>?> GetMapAsync(string path, CancellationToken ct = default);

	// Reads a scalar RTDB node (e.g. "timer", a raw number) directly as T — unlike GetMapAsync,
	// the path's value isn't an object of children. Returns default(T) for an empty/nonexistent path.
	Task<T?> GetValueAsync<T>(string path, CancellationToken ct = default);
}

// Same authenticated-REST-against-RTDB pattern the old FirebasePlayerRepository used (Phase 0/1,
// since removed from the API itself once Player moved to Postgres) — reused here since the
// migrator is the one remaining piece that legitimately needs to read Firebase directly.
public class RtdbReader(HttpClient httpClient, GoogleCredential credential) : IRtdbReader
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
	{
		PropertyNameCaseInsensitive = true,
	};

	public async Task<Dictionary<string, JsonElement>?> GetMapAsync(string path, CancellationToken ct = default)
	{
		var token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);

		using var request = new HttpRequestMessage(HttpMethod.Get, $"{path}.json");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

		using var response = await httpClient.SendAsync(request, ct);
		response.EnsureSuccessStatusCode();

		return await response.Content.ReadFromJsonAsync<Dictionary<string, JsonElement>>(JsonOptions, ct);
	}

	public async Task<T?> GetValueAsync<T>(string path, CancellationToken ct = default)
	{
		var token = await credential.UnderlyingCredential.GetAccessTokenForRequestAsync(cancellationToken: ct);

		using var request = new HttpRequestMessage(HttpMethod.Get, $"{path}.json");
		request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

		using var response = await httpClient.SendAsync(request, ct);
		response.EnsureSuccessStatusCode();

		return await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
	}

	public static T? Parse<T>(JsonElement element) => element.Deserialize<T>(JsonOptions);
}
