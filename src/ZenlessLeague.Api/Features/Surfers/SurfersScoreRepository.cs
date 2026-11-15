using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Features.Surfers;

public record SurfersScoreRow(string Uid, string Name, string? Avatar, int Score, DateTimeOffset AchievedAt);

public interface ISurfersScoreRepository
{
	Task<SurfersScore?> GetAsync(Guid playerId, CancellationToken ct = default);
	Task CreateAsync(SurfersScore score, CancellationToken ct = default);
	Task SaveChangesAsync(CancellationToken ct = default);
	Task<int> CountHigherThanAsync(int score, CancellationToken ct = default);
	Task<List<SurfersScoreRow>> ListTopAsync(int limit, CancellationToken ct = default);
}

public class SurfersScoreRepository(AppDbContext context) : ISurfersScoreRepository
{
	public Task<SurfersScore?> GetAsync(Guid playerId, CancellationToken ct = default) =>
		context.SurfersScores.FirstOrDefaultAsync(s => s.PlayerId == playerId, ct);

	public async Task CreateAsync(SurfersScore score, CancellationToken ct = default)
	{
		context.SurfersScores.Add(score);
		await context.SaveChangesAsync(ct);
	}

	public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);

	public Task<int> CountHigherThanAsync(int score, CancellationToken ct = default) =>
		context.SurfersScores.CountAsync(s => s.Score > score, ct);

	public Task<List<SurfersScoreRow>> ListTopAsync(int limit, CancellationToken ct = default) =>
		context.SurfersScores.AsNoTracking()
			.Join(context.Players, s => s.PlayerId, p => p.Id, (s, p) => new { s, p })
			.OrderByDescending(x => x.s.Score).ThenBy(x => x.s.AchievedAt)
			.Take(limit)
			.Select(x => new SurfersScoreRow(x.p.Uid, x.p.Name, x.p.Avatar, x.s.Score, x.s.AchievedAt))
			.ToListAsync(ct);
}
