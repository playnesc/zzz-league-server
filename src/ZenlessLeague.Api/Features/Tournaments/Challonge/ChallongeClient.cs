using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ZenlessLeague.Api.Features.Tournaments.Challonge;

public record ChallongeCreatedTournament(string ChallongeTournamentId, string? ChallongeTournamentUrl);
public record ChallongeParticipantResult(string ChallongeParticipantId, string? Misc, int? FinalRank);

public record ChallongeMatchResult(
	string ChallongeMatchId, int Round, int? SuggestedPlayOrder, string State,
	string? WinnerChallongeParticipantId, string? P1ChallongeParticipantId, string? P2ChallongeParticipantId);

public class ChallongeApiException(int statusCode, string body) : Exception($"Challonge API error ({statusCode}): {body}")
{
	public int StatusCode { get; } = statusCode;
}

public interface IChallongeClient
{
	Task<ChallongeCreatedTournament> CreateTournamentAsync(
		string name, string tournamentType, string? description, DateTimeOffset startsAt,
		int? consolationMatchesTargetRank, CancellationToken ct = default);

	Task BulkAddParticipantsAsync(string challongeTournamentId, IReadOnlyList<(string Name, string Uid)> participants, CancellationToken ct = default);

	Task<List<ChallongeParticipantResult>> RandomizeParticipantsAsync(
		string challongeTournamentId, IReadOnlyList<(string Name, string Uid)> participants, CancellationToken ct = default);

	Task<List<ChallongeMatchResult>> GetMatchesAsync(string challongeTournamentId, CancellationToken ct = default);

	Task<List<ChallongeParticipantResult>> GetAllParticipantsAsync(string challongeTournamentId, CancellationToken ct = default);

	Task<bool> StartAsync(string challongeTournamentId, CancellationToken ct = default);
	Task<bool> FinalizeAsync(string challongeTournamentId, CancellationToken ct = default);
	Task<bool> ResetAsync(string challongeTournamentId, CancellationToken ct = default);

	Task UpdateMatchResultAsync(
		string challongeTournamentId, string matchId, string p1ParticipantId, string p2ParticipantId,
		bool p1Win, string loserScore, CancellationToken ct = default);

	Task DeleteTournamentAsync(string challongeTournamentId, CancellationToken ct = default);
}

public class ChallongeClient(HttpClient httpClient, IOptions<ChallongeOptions> options, ILogger<ChallongeClient> logger) : IChallongeClient
{
	private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

	// For the HTTP retry policy (see ThirdPartyHttpResilience): a PUT that must still never be
	// re-sent — repeating randomize reshuffles the seeding instead of being a no-op.
	public static bool IsNonIdempotent(HttpRequestMessage request) =>
		request.Method == HttpMethod.Put && request.RequestUri?.AbsolutePath.EndsWith("/randomize.json", StringComparison.Ordinal) == true;

	public async Task<ChallongeCreatedTournament> CreateTournamentAsync(
		string name, string tournamentType, string? description, DateTimeOffset startsAt,
		int? consolationMatchesTargetRank, CancellationToken ct = default)
	{
		var attributes = new Dictionary<string, object?>
		{
			["name"] = name,
			["tournament_type"] = tournamentType,
			["description"] = description ?? "",
			["private"] = false,
			["starts_at"] = startsAt.ToUniversalTime().ToString("o"),
		};

		if (tournamentType == "double elimination")
		{
			attributes["double_elimination_options"] = new { grand_finals_modifier = "single match" };
		}

		if (consolationMatchesTargetRank is not null)
		{
			attributes["match_options"] = new { consolation_matches_target_rank = consolationMatchesTargetRank };
		}

		var response = await SendAsync(HttpMethod.Post, "tournaments.json",
			new { data = new { type = "tournament", attributes } }, ct);
		var body = await ReadAsync<ChallongeTournamentResponse>(response, ct);

		return new ChallongeCreatedTournament(body.Data.Id, body.Data.Attributes.FullChallongeUrl);
	}

	public async Task BulkAddParticipantsAsync(string challongeTournamentId, IReadOnlyList<(string Name, string Uid)> participants, CancellationToken ct = default)
	{
		var response = await SendAsync(HttpMethod.Post, $"tournaments/{challongeTournamentId}/participants/bulk_add.json",
			new
			{
				data = new
				{
					type = "Participant",
					attributes = new { participants = participants.Select(p => new { name = p.Name, misc = p.Uid }) },
				},
			}, ct);
		await ThrowIfErrorAsync(response, ct);
	}

	public async Task<List<ChallongeParticipantResult>> RandomizeParticipantsAsync(
		string challongeTournamentId, IReadOnlyList<(string Name, string Uid)> participants, CancellationToken ct = default)
	{
		var response = await SendAsync(HttpMethod.Put, $"tournaments/{challongeTournamentId}/participants/randomize.json",
			new
			{
				data = new
				{
					type = "Participant",
					attributes = new { participants = participants.Select(p => new { name = p.Name, misc = p.Uid }) },
				},
			}, ct);
		var body = await ReadAsync<ChallongeParticipantsResponse>(response, ct);

		return body.Data.Select(d => new ChallongeParticipantResult(d.Id, d.Attributes.Misc, d.Attributes.FinalRank)).ToList();
	}

	// Follows links.next like GetAllParticipantsAsync — a page-limited response would otherwise
	// silently drop matches (and make SyncFromChallongeAsync think the bracket was fully played).
	// A single-page response simply has no next link.
	public async Task<List<ChallongeMatchResult>> GetMatchesAsync(string challongeTournamentId, CancellationToken ct = default)
	{
		var pages = await GetAllPagesAsync<ChallongeMatchData>($"tournaments/{challongeTournamentId}/matches.json", d => d.Id, ct);

		return pages.Select(d =>
		{
			var pp = d.Attributes.PointsByParticipant ?? [];
			return new ChallongeMatchResult(
				d.Id, d.Attributes.Round, d.Attributes.SuggestedPlayOrder, d.Attributes.State,
				d.Attributes.WinnerId,
				pp.ElementAtOrDefault(0)?.ParticipantId,
				pp.ElementAtOrDefault(1)?.ParticipantId);
		}).ToList();
	}

	public async Task<List<ChallongeParticipantResult>> GetAllParticipantsAsync(string challongeTournamentId, CancellationToken ct = default)
	{
		var pages = await GetAllPagesAsync<ChallongeParticipantData>($"tournaments/{challongeTournamentId}/participants.json", d => d.Id, ct);
		return pages.Select(d => new ChallongeParticipantResult(d.Id, d.Attributes.Misc, d.Attributes.FinalRank)).ToList();
	}

	// Walks a paginated list endpoint, de-duplicating by id (first occurrence wins, original order
	// kept). Stops at an empty page, once meta.count items are collected, when a page adds nothing
	// new (guards against a next link that loops), or when there's no next link. A next link is only
	// followed on Challonge's own host, since every request carries the API key.
	private async Task<List<T>> GetAllPagesAsync<T>(string firstPath, Func<T, string> idOf, CancellationToken ct)
	{
		var items = new List<T>();
		var seenIds = new HashSet<string>();
		string? url = firstPath;
		var firstPage = true;

		while (url is not null)
		{
			var response = await SendAsync(HttpMethod.Get, url, null, ct, absoluteUrl: !firstPage);
			firstPage = false;
			var body = await ReadAsync<ChallongePageResponse<T>>(response, ct);

			var added = 0;
			foreach (var item in body.Data)
			{
				if (seenIds.Add(idOf(item)))
				{
					items.Add(item);
					added++;
				}
			}

			if (added == 0 || (body.Meta?.Count is int count && items.Count >= count))
			{
				break;
			}

			url = body.Links?.Next is string next && IsChallongeUrl(next) ? next : null;
		}

		return items;
	}

	private bool IsChallongeUrl(string url) =>
		Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
		uri.Scheme == Uri.UriSchemeHttps &&
		string.Equals(uri.Host, httpClient.BaseAddress?.Host, StringComparison.OrdinalIgnoreCase);

	public async Task<bool> StartAsync(string challongeTournamentId, CancellationToken ct = default)
	{
		var response = await SendAsync(HttpMethod.Put, $"tournaments/{challongeTournamentId}/change_state.json",
			new { data = new { type = "TournamentState", attributes = new { state = "start" } } }, ct);

		if (response.IsSuccessStatusCode)
		{
			return true;
		}

		if ((int)response.StatusCode == 422)
		{
			var error = await ReadAsync<ChallongeErrorResponse>(response, ct, allowFailure: true);
			if (error.Errors?.Any(e => e.Source?.Pointer == "/data/attributes/state") == true)
			{
				return false;
			}
		}

		await ThrowIfErrorAsync(response, ct);
		return true; // unreachable — ThrowIfErrorAsync always throws for a non-success response
	}

	public async Task<bool> FinalizeAsync(string challongeTournamentId, CancellationToken ct = default)
	{
		var response = await SendAsync(HttpMethod.Put, $"tournaments/{challongeTournamentId}/change_state.json",
			new { data = new { type = "TournamentState", attributes = new { state = "finalize" } } }, ct);

		if (response.IsSuccessStatusCode)
		{
			return true;
		}

		if ((int)response.StatusCode == 422)
		{
			var error = await ReadAsync<ChallongeErrorResponse>(response, ct, allowFailure: true);
			if (error.Errors?.Any(e => e.Source?.Pointer == "/data/attributes/state") == true)
			{
				return false;
			}
		}

		await ThrowIfErrorAsync(response, ct);
		return true; // unreachable — ThrowIfErrorAsync always throws for a non-success response
	}

	public async Task<bool> ResetAsync(string challongeTournamentId, CancellationToken ct = default)
	{
		var response = await SendAsync(HttpMethod.Put, $"tournaments/{challongeTournamentId}/change_state.json",
			new { data = new { type = "TournamentState", attributes = new { state = "reset" } } }, ct);

		if (response.IsSuccessStatusCode)
		{
			return true;
		}

		if (response.StatusCode == HttpStatusCode.NotFound)
		{
			return false;
		}

		if ((int)response.StatusCode == 422)
		{
			var error = await ReadAsync<ChallongeErrorResponse>(response, ct, allowFailure: true);
			if (error.Errors?.Any(e => e.Source?.Pointer == "/data/attributes/state") == true)
			{
				return false;
			}
		}

		await ThrowIfErrorAsync(response, ct);
		return true; // unreachable — ThrowIfErrorAsync always throws for a non-success response
	}

	public async Task UpdateMatchResultAsync(
		string challongeTournamentId, string matchId, string p1ParticipantId, string p2ParticipantId,
		bool p1Win, string loserScore, CancellationToken ct = default)
	{
		var response = await SendAsync(HttpMethod.Put, $"tournaments/{challongeTournamentId}/matches/{matchId}.json",
			new
			{
				data = new
				{
					type = "Match",
					attributes = new
					{
						match = new object[]
						{
							new { participant_id = p1ParticipantId, score_set = p1Win ? "1" : loserScore, rank = p1Win ? 1 : 2, advancing = p1Win },
							new { participant_id = p2ParticipantId, score_set = p1Win ? loserScore : "1", rank = p1Win ? 2 : 1, advancing = !p1Win },
						},
					},
				},
			}, ct);
		await ThrowIfErrorAsync(response, ct);
	}

	public async Task DeleteTournamentAsync(string challongeTournamentId, CancellationToken ct = default)
	{
		var response = await SendAsync(HttpMethod.Delete, $"tournaments/{challongeTournamentId}.json", null, ct);
		if (response.StatusCode == HttpStatusCode.NotFound)
		{
			return;
		}

		await ThrowIfErrorAsync(response, ct);
	}

	private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string pathOrUrl, object? body, CancellationToken ct, bool absoluteUrl = false)
	{
		var apiKey = options.Value.ApiKey ?? throw new InvalidOperationException("Challonge:ApiKey is not configured.");

		using var request = new HttpRequestMessage(method, absoluteUrl ? new Uri(pathOrUrl) : new Uri(pathOrUrl, UriKind.Relative));
		request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
		request.Headers.Add("Authorization-Type", "v1");
		request.Headers.Add("Authorization", apiKey);
		var jsonApiMediaType = new MediaTypeHeaderValue("application/vnd.api+json");
		if (body is not null)
		{
			request.Content = JsonContent.Create(body, jsonApiMediaType, JsonOptions);
		}
		else
		{
			// Challonge requires Content-Type on every request, including bodyless GETs — omitting
			// it gets a 415 that confusingly blames "Content-Type" for being blank rather than
			// saying it's missing.
			request.Content = new ByteArrayContent([]);
			request.Content.Headers.ContentType = jsonApiMediaType;
		}

		var response = await httpClient.SendAsync(request, ct);

		if (!response.IsSuccessStatusCode)
		{
			// Request headers are deliberately not logged — they carry the API key (Authorization).
			// Content headers are safe and are what diagnosed the old blank-Content-Type 415s.
			await response.Content.LoadIntoBufferAsync(ct);
			var contentHeaders = string.Join("; ", request.Content.Headers.Select(h => $"{h.Key}=[{string.Join("|", h.Value)}]"));
			var responseBody = await response.Content.ReadAsStringAsync(ct);
			logger.LogWarning(
				"Challonge API call failed: {Method} {Path} -> {StatusCode}\nContent headers: {ContentHeaders}\nResponse body: {ResponseBody}",
				method, pathOrUrl, (int)response.StatusCode, contentHeaders,
				responseBody.Length > 4000 ? responseBody[..4000] + "…" : responseBody);
		}

		return response;
	}

	private static async Task ThrowIfErrorAsync(HttpResponseMessage response, CancellationToken ct)
	{
		if (response.IsSuccessStatusCode)
		{
			return;
		}

		var body = await response.Content.ReadAsStringAsync(ct);
		throw new ChallongeApiException((int)response.StatusCode, body);
	}

	private static async Task<T> ReadAsync<T>(HttpResponseMessage response, CancellationToken ct, bool allowFailure = false)
	{
		if (!allowFailure)
		{
			await ThrowIfErrorAsync(response, ct);
		}

		var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions, ct);
		return result ?? throw new InvalidOperationException("Challonge API returned an empty response body.");
	}

	private record ChallongeTournamentResponse(ChallongeTournamentData Data);
	private record ChallongeTournamentData(string Id, ChallongeTournamentAttributes Attributes);
	private record ChallongeTournamentAttributes([property: JsonPropertyName("full_challonge_url")] string? FullChallongeUrl);

	private record ChallongeParticipantsResponse(List<ChallongeParticipantData> Data);
	private record ChallongeParticipantData(string Id, ChallongeParticipantAttributes Attributes);
	private record ChallongeParticipantAttributes(string? Misc, [property: JsonPropertyName("final_rank")] int? FinalRank);

	private record ChallongePageResponse<T>(List<T> Data, ChallongeLinks? Links, ChallongeMeta? Meta);
	private record ChallongeLinks(string? Next);
	private record ChallongeMeta(int? Count);

	private record ChallongeMatchData(string Id, ChallongeMatchAttributes Attributes);
	private record ChallongeMatchAttributes(
		int Round,
		[property: JsonPropertyName("suggested_play_order")] int? SuggestedPlayOrder,
		string State,
		[property: JsonPropertyName("winner_id"), JsonConverter(typeof(FlexibleStringConverter))] string? WinnerId,
		[property: JsonPropertyName("points_by_participant")] List<ChallongePointsByParticipant>? PointsByParticipant);
	private record ChallongePointsByParticipant(
		[property: JsonPropertyName("participant_id"), JsonConverter(typeof(FlexibleStringConverter))] string? ParticipantId);

	// Challonge sends participant/match identifier fields as raw JSON numbers in some responses
	// (e.g. points_by_participant[].participant_id) and as strings elsewhere — normalize both to string.
	private class FlexibleStringConverter : JsonConverter<string?>
	{
		public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
			reader.TokenType switch
			{
				JsonTokenType.String => reader.GetString(),
				JsonTokenType.Number => reader.TryGetInt64(out var l) ? l.ToString() : reader.GetDouble().ToString(),
				JsonTokenType.Null => null,
				_ => throw new JsonException($"Unexpected token {reader.TokenType} for flexible string field."),
			};

		public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options) =>
			writer.WriteStringValue(value);
	}

	private record ChallongeErrorResponse(List<ChallongeError>? Errors);
	private record ChallongeError(ChallongeErrorSource? Source);
	private record ChallongeErrorSource(string? Pointer);
}
