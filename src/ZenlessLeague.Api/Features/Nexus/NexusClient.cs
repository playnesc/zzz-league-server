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
	// Raw JSON of zzz.nexus's playnesc integration endpoint for one draft (= one game; the same id
	// its /drafts/{id} and /results/{id}/obs pages use). Nexus only answers our server's IP (anyone
	// else gets a Cloudflare 403), hence server-side.
	Task<string> GetDraftResultJsonAsync(string draftId, CancellationToken ct = default);
}

public class NexusClient(HttpClient httpClient, ILogger<NexusClient> logger) : INexusClient
{
	public async Task<string> GetDraftResultJsonAsync(string draftId, CancellationToken ct = default)
	{
		HttpResponseMessage response;
		try
		{
			response = await httpClient.GetAsync($"api/integrations/playnesc/drafts/{Uri.EscapeDataString(draftId)}/results", ct);
		}
		catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
		{
			logger.LogWarning(ex, "Nexus request for draft {DraftId} failed", draftId);
			throw new NexusUnavailableException("Nexus is unavailable right now, try again later.");
		}

		using (response)
		{
			if (response.StatusCode == HttpStatusCode.NotFound)
			{
				throw new NexusResultException("Nexus result not found.");
			}

			// The integration endpoint is IP-whitelisted — a 403 most likely means our server's IP
			// changed (new Hetzner box, Cloudflare in front of us) and Nexus has to whitelist it again.
			if (response.StatusCode == HttpStatusCode.Forbidden)
			{
				logger.LogError("Nexus refused draft {DraftId} with 403 — is this server's IP still whitelisted by Nexus?", draftId);
				throw new NexusUnavailableException("Nexus is unavailable right now, try again later.");
			}

			if (!response.IsSuccessStatusCode)
			{
				logger.LogWarning("Nexus returned {StatusCode} for draft {DraftId}", (int)response.StatusCode, draftId);
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
