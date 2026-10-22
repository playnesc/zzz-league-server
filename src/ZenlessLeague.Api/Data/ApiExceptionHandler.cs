using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ZenlessLeague.Api.Data;

// Replaces the try/catch blocks that used to be repeated in every endpoint file. Known domain
// exceptions (ApiException and its subclasses) map to their declared status code with their own
// message; anything else is logged in full server-side but never reaches the client verbatim —
// only a generic message does, so a bug never leaks internals (stack traces, SQL, file paths).
public class ApiExceptionHandler(ILogger<ApiExceptionHandler> logger) : IExceptionHandler
{
	public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken ct)
	{
		if (exception is ApiException apiException)
		{
			await WriteAsync(httpContext, apiException.StatusCode, apiException.Message, ct);
			return true;
		}

		// Database conflicts are a client-visible condition (lost a race, duplicate, still-referenced
		// row), not a server bug — 409 with a generic message instead of a 500. Constraint names and
		// SQL stay in the log only.
		if (ConflictMessage(exception) is string conflictMessage)
		{
			logger.LogWarning(exception, "Database conflict on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
			await WriteAsync(httpContext, StatusCodes.Status409Conflict, conflictMessage, ct);
			return true;
		}

		logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
		await WriteAsync(httpContext, StatusCodes.Status500InternalServerError, "An unexpected error occurred.", ct);
		return true;
	}

	internal static string? ConflictMessage(Exception exception) => exception switch
	{
		// Optimistic concurrency (Version/xmin) — someone else saved the same row first.
		DbUpdateConcurrencyException => "This was changed by someone else at the same time — refresh and try again.",
		DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } } =>
			"That conflicts with an existing record — it may already exist.",
		DbUpdateException { InnerException: PostgresException pg } when IsStillReferenced(pg) =>
			"This can't be done because other records still reference it.",
		_ => null,
	};

	// Deleting a row that's still referenced raises 23001 (restrict_violation) for ON DELETE RESTRICT
	// keys — which is every Restrict FK in AppDbContext — and 23503 (foreign_key_violation) otherwise.
	public static bool IsStillReferenced(PostgresException exception) =>
		exception.SqlState is PostgresErrorCodes.RestrictViolation or PostgresErrorCodes.ForeignKeyViolation;

	private static async Task WriteAsync(HttpContext httpContext, int statusCode, string message, CancellationToken ct)
	{
		httpContext.Response.StatusCode = statusCode;
		await httpContext.Response.WriteAsJsonAsync(new { error = message }, ct);
	}
}
