namespace ZenlessLeague.Api.Data;

// Common ancestor for every feature's domain exception (HistoryValidationException,
// MatchForbiddenException, etc.) so ApiExceptionHandler can map all of them to an HTTP
// response in one place instead of each endpoint file repeating its own try/catch.
public abstract class ApiException(int statusCode, string message) : Exception(message)
{
	public int StatusCode { get; } = statusCode;
}

public class ValidationApiException(string message) : ApiException(StatusCodes.Status400BadRequest, message);

public class ForbiddenApiException(string message) : ApiException(StatusCodes.Status403Forbidden, message);

public class NotFoundApiException(string message) : ApiException(StatusCodes.Status404NotFound, message);
