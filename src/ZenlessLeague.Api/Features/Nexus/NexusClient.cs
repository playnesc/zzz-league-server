using System.Net;
using ZenlessLeague.Api.Infrastructure.Errors;

namespace ZenlessLeague.Api.Features.Nexus;

/// <summary>The user's link/result is the problem (not a Nexus link, not found, wrong game mode) — maps to 400.</summary>
public class NexusResultException(string message) : ValidationApiException(message);

/// <summary>
/// Nexus answered, but not in the shape we parse — most likely Nexus changed its (undocumented)
/// format. Maps to 502 so the client can tell users to report it to the admins; MatchService logs
/// the raw response.
/// </summary>
public class NexusFormatException(string message, Exception? innerException = null)
	: ApiException(StatusCodes.Status502BadGateway, message, innerException);

/// <summary>Nexus is down or erroring — maps to 503 ("try again later").</summary>
public class NexusUnavailableException(string message) : ApiException(StatusCodes.Status503ServiceUnavailable, message);

public interface INexusClient
{
	// Raw JSON of nexus-shiyu.com's (undocumented) /api/results/{id} endpoint — the same data its
	// /results/{id}/obs overlay renders. Public, no auth, but no CORS headers, hence server-side.
	Task<string> GetResultJsonAsync(string resultId, CancellationToken ct = default);
}

public class NexusClient(HttpClient httpClient, ILogger<NexusClient> logger) : INexusClient
{
	public async Task<string> GetResultJsonAsync(string resultId, CancellationToken ct = default)
	{
		HttpResponseMessage response;
		try
		{
			response = await httpClient.GetAsync($"api/results/{Uri.EscapeDataString(resultId)}", ct);
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
		{
			logger.LogWarning(ex, "Nexus request for result {ResultId} failed", resultId);
			throw new NexusUnavailableException("Nexus is unavailable right now, try again later.");
		}

		using (response)
		{
			if (response.StatusCode == HttpStatusCode.NotFound)
			{
				throw new NexusResultException("Nexus result not found.");
			}

			if (!response.IsSuccessStatusCode)
			{
				logger.LogWarning("Nexus returned {StatusCode} for result {ResultId}", (int)response.StatusCode, resultId);
				throw new NexusUnavailableException("Nexus is unavailable right now, try again later.");
			}

			// A 200 that isn't JSON means the endpoint changed (e.g. now serves a page or a login wall).
			if (response.Content.Headers.ContentType?.MediaType != "application/json")
			{
				throw new NexusFormatException(
					$"Nexus answered with '{response.Content.Headers.ContentType?.MediaType}' instead of JSON.");
			}

			return await response.Content.ReadAsStringAsync(ct);
		}
	}
}
