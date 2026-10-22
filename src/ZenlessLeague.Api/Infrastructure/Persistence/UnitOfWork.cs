using Microsoft.EntityFrameworkCore;

namespace ZenlessLeague.Api.Infrastructure.Persistence;

public interface IUnitOfWork
{
	// Runs `work` inside one database transaction: every SaveChanges it makes (across any of the
	// repositories sharing this request's DbContext) commits together, or — if it throws — none do.
	// Keep slow external calls (Challonge, Discord) outside where possible; they can't be rolled back
	// anyway and only hold the transaction open.
	Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default);
}

public class EfUnitOfWork(AppDbContext db) : IUnitOfWork
{
	public async Task<T> InTransactionAsync<T>(Func<Task<T>> work, CancellationToken ct = default)
	{
		// Joins an enclosing transaction rather than nesting; the InMemory provider used by unit
		// tests has no transactions at all.
		if (!db.Database.IsRelational() || db.Database.CurrentTransaction is not null)
		{
			return await work();
		}

		await using var transaction = await db.Database.BeginTransactionAsync(ct);
		var result = await work();
		await transaction.CommitAsync(ct);
		return result;
	}
}

public static class UnitOfWorkExtensions
{
	public static Task InTransactionAsync(this IUnitOfWork unitOfWork, Func<Task> work, CancellationToken ct = default) =>
		unitOfWork.InTransactionAsync(async () =>
		{
			await work();
			return true;
		}, ct);
}
