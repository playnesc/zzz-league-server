using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using ZenlessLeague.Api.Infrastructure.Authorization;

namespace ZenlessLeague.Api.Infrastructure.Http;

// Abuse brakes, not quotas: the limits sit far above what someone clicking through the site does,
// and only on the endpoints anyone can reach — admin/moderator endpoints aren't limited. Reads
// aren't limited either.
public static class RateLimits
{
	// Anonymous Discord sign-in — every attempt costs a Discord OAuth round-trip. Per client IP
	// (the browser calls it directly; see ForwardedHeaders in Program.cs for how the real IP gets here).
	public const string DiscordLogin = "discord-login";

	// Writes any signed-in user can make: uploads, contributions/reviews/comments, registrations,
	// match results, profile and team edits. Per user, not per IP — some of these (e.g. the session
	// exchange) arrive from the SvelteKit server, so every user would share one IP.
	public const string UserWrites = "user-writes";

	public const int DiscordLoginPermitsPerMinute = 10;
	public const int UserWritesPermitsPerMinute = 30;

	public static IServiceCollection AddApiRateLimiting(this IServiceCollection services) =>
		services.AddRateLimiter(options =>
		{
			options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
			options.OnRejected = async (context, ct) =>
			{
				if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
				{
					context.HttpContext.Response.Headers.RetryAfter =
						((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
				}

				// Same { error } shape as ApiExceptionHandler, so the frontend shows it like any other error.
				await context.HttpContext.Response.WriteAsJsonAsync(new { error = "Too many requests — wait a minute and try again." }, ct);
			};

			options.AddPolicy(DiscordLogin, http => PerMinute(ClientIp(http), DiscordLoginPermitsPerMinute));
			options.AddPolicy(UserWrites, http => PerMinute(http.User.GetUid() ?? ClientIp(http), UserWritesPermitsPerMinute));
		});

	private static string ClientIp(HttpContext http) => http.Connection.RemoteIpAddress?.ToString() ?? "unknown";

	private static RateLimitPartition<string> PerMinute(string key, int permits) =>
		RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
		{
			PermitLimit = permits,
			Window = TimeSpan.FromMinutes(1),
		});
}
