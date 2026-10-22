using System.Net;
using Microsoft.Extensions.Http.Resilience;
using Polly;

namespace ZenlessLeague.Api.Infrastructure.Http;

// Shared retry/timeout policy for the third-party typed HttpClients (Discord, Challonge, Firebase
// Storage), on top of Microsoft.Extensions.Http.Resilience's standard pipeline: retries transient
// failures (5xx, 408, 429, network errors) with exponential backoff + jitter, bounds each attempt
// and the whole call with timeouts (instead of HttpClient's 100s default), and has a circuit
// breaker for a service that keeps failing.
//
// Only requests that are safe to send twice are retried. POST/PATCH never are: if the first
// attempt reached the server but its response got lost, a retry would create a second Challonge
// tournament or Discord role/channel, or replay a single-use OAuth code. `isNonIdempotent`
// excludes further requests a client knows aren't repeatable (e.g. Challonge's randomize PUT).
public static class ThirdPartyHttpResilience
{
	public static IHttpClientBuilder AddThirdPartyResilience(
		this IHttpClientBuilder builder,
		TimeSpan attemptTimeout,
		bool retryOnTooManyRequests = true,
		Func<HttpRequestMessage, bool>? isNonIdempotent = null,
		TimeSpan? retryBaseDelay = null)
	{
		builder.AddStandardResilienceHandler(options =>
		{
			options.AttemptTimeout.Timeout = attemptTimeout;
			// Room for every attempt (1 + 3 retries) plus the backoff between them.
			options.TotalRequestTimeout.Timeout = attemptTimeout * 4 + TimeSpan.FromSeconds(15);
			// The standard pipeline requires the breaker's sampling window to be >= 2x the attempt timeout.
			options.CircuitBreaker.SamplingDuration = TimeSpan.FromTicks(Math.Max(attemptTimeout.Ticks * 2, TimeSpan.FromSeconds(30).Ticks));

			if (retryBaseDelay is TimeSpan delay)
			{
				options.Retry.Delay = delay;
			}

			options.Retry.DisableFor(HttpMethod.Post, HttpMethod.Patch);
			var shouldHandle = options.Retry.ShouldHandle;
			options.Retry.ShouldHandle = args =>
			{
				if (!retryOnTooManyRequests && args.Outcome.Result?.StatusCode == HttpStatusCode.TooManyRequests)
				{
					return ValueTask.FromResult(false);
				}

				var request = args.Context.GetRequestMessage();
				if (request is not null && isNonIdempotent?.Invoke(request) == true)
				{
					return ValueTask.FromResult(false);
				}

				return shouldHandle(args);
			};
		});

		return builder;
	}
}
