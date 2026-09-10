using Microsoft.AspNetCore.Diagnostics;

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
			httpContext.Response.StatusCode = apiException.StatusCode;
			await httpContext.Response.WriteAsJsonAsync(new { error = apiException.Message }, ct);
			return true;
		}

		logger.LogError(exception, "Unhandled exception on {Method} {Path}", httpContext.Request.Method, httpContext.Request.Path);
		httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;
		await httpContext.Response.WriteAsJsonAsync(new { error = "An unexpected error occurred." }, ct);
		return true;
	}
}
