using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Infrastructure.Persistence;

namespace ZenlessLeague.Api.Features.Seasons;

public interface ISeasonTimerRepository
{
	Task<DateTimeOffset?> GetEndTimestampAsync(CancellationToken ct = default);
	Task SetEndTimestampAsync(DateTimeOffset endTimestamp, CancellationToken ct = default);
}

public class SeasonTimerRepository(AppDbContext context) : ISeasonTimerRepository
{
	public async Task<DateTimeOffset?> GetEndTimestampAsync(CancellationToken ct = default)
	{
		var timer = await context.SeasonTimers.FindAsync([SeasonTimer.SingletonId], ct);
		return timer?.EndTimestamp;
	}

	public async Task SetEndTimestampAsync(DateTimeOffset endTimestamp, CancellationToken ct = default)
	{
		var timer = await context.SeasonTimers.FindAsync([SeasonTimer.SingletonId], ct);
		if (timer is null)
		{
			context.SeasonTimers.Add(new SeasonTimer { EndTimestamp = endTimestamp });
		}
		else
		{
			timer.EndTimestamp = endTimestamp;
		}

		await context.SaveChangesAsync(ct);
	}
}
