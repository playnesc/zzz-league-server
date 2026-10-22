using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using ZenlessLeague.Api.Data;

namespace ZenlessLeague.Api.Tests;

public class ApiExceptionHandlerTests
{
	public static TheoryData<Exception, int> Cases => new()
	{
		{ new ValidationApiException("bad input"), StatusCodes.Status400BadRequest },
		{ new DbUpdateConcurrencyException("stale"), StatusCodes.Status409Conflict },
		{ new DbUpdateException("dup", new PostgresException("duplicate key value violates unique constraint \"IX_Players_Name\"", "ERROR", "ERROR", PostgresErrorCodes.UniqueViolation)), StatusCodes.Status409Conflict },
		{ new DbUpdateException("fk", new PostgresException("violates foreign key constraint \"FK_HistoryEntries_Players_P1PlayerId\"", "ERROR", "ERROR", PostgresErrorCodes.ForeignKeyViolation)), StatusCodes.Status409Conflict },
		// What deleting a still-referenced row actually raises for ON DELETE RESTRICT keys.
		{ new DbUpdateException("restrict", new PostgresException("violates RESTRICT setting of foreign key constraint \"FK_HistoryEntries_Players_P1PlayerId\"", "ERROR", "ERROR", PostgresErrorCodes.RestrictViolation)), StatusCodes.Status409Conflict },
		{ new DbUpdateException("other", new PostgresException("disk full", "ERROR", "ERROR", PostgresErrorCodes.DiskFull)), StatusCodes.Status500InternalServerError },
		{ new InvalidOperationException("bug"), StatusCodes.Status500InternalServerError },
	};

	[Theory]
	[MemberData(nameof(Cases))]
	public async Task TryHandleAsync_MapsExceptionToStatusWithoutLeakingDatabaseDetails(Exception exception, int expectedStatus)
	{
		var context = new DefaultHttpContext();
		context.Response.Body = new MemoryStream();
		var handler = new ApiExceptionHandler(NullLogger<ApiExceptionHandler>.Instance);

		Assert.True(await handler.TryHandleAsync(context, exception, CancellationToken.None));

		Assert.Equal(expectedStatus, context.Response.StatusCode);
		var body = Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray());
		Assert.DoesNotContain("constraint", body);
		Assert.DoesNotContain("IX_", body);
	}
}
