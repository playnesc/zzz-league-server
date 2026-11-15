using Microsoft.Extensions.Options;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Infrastructure.Errors;

namespace ZenlessLeague.Api.Features.Surfers;

public interface ISurfersService
{
	// Null when no player exists for uid.
	Task<SubmitSurfersScoreResponse?> SubmitAsync(string uid, SubmitSurfersScoreRequest request, CancellationToken ct = default);
	Task<List<SurfersLeaderboardEntryDto>> GetLeaderboardAsync(int limit, CancellationToken ct = default);
	// Null when the player doesn't exist or hasn't scored yet.
	Task<SurfersMyScoreDto?> GetMineAsync(string uid, CancellationToken ct = default);
}

/// <summary>Thrown for scores that can't be real (negative, too long a run, too fast) — maps to 400.</summary>
public class SurfersValidationException(string message) : ValidationApiException(message);

public class SurfersService(
	ISurfersScoreRepository scoreRepository,
	IPlayerRepository playerRepository,
	IOptions<SurfersOptions> options,
	ILogger<SurfersService> logger) : ISurfersService
{
	public const int DefaultLimit = 50;
	public const int MaxLimit = 100;

	public async Task<SubmitSurfersScoreResponse?> SubmitAsync(string uid, SubmitSurfersScoreRequest request, CancellationToken ct = default)
	{
		Validate(uid, request);

		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		if (player is null)
		{
			return null;
		}

		var best = await scoreRepository.GetAsync(player.Id, ct);
		if (request.Score <= (best?.Score ?? 0))
		{
			return new SubmitSurfersScoreResponse(best?.Score ?? 0, false, best is null ? null : await RankOfAsync(best.Score, ct));
		}

		var now = DateTimeOffset.UtcNow;
		if (best is null)
		{
			best = new SurfersScore { PlayerId = player.Id, Score = request.Score, DurationSeconds = request.DurationSeconds, AchievedAt = now };
			await scoreRepository.CreateAsync(best, ct);
		}
		else
		{
			best.Score = request.Score;
			best.DurationSeconds = request.DurationSeconds;
			best.AchievedAt = now;
			await scoreRepository.SaveChangesAsync(ct);
		}

		return new SubmitSurfersScoreResponse(best.Score, true, await RankOfAsync(best.Score, ct));
	}

	public async Task<List<SurfersLeaderboardEntryDto>> GetLeaderboardAsync(int limit, CancellationToken ct = default)
	{
		var rows = await scoreRepository.ListTopAsync(Math.Clamp(limit, 1, MaxLimit), ct);

		// Competition ranking (1, 2, 2, 4): the list starts at the top, so a row's rank is its
		// position unless it ties the row above it.
		var entries = new List<SurfersLeaderboardEntryDto>(rows.Count);
		for (var i = 0; i < rows.Count; i++)
		{
			var rank = i > 0 && rows[i].Score == rows[i - 1].Score ? entries[i - 1].Rank : i + 1;
			var row = rows[i];
			entries.Add(new SurfersLeaderboardEntryDto(rank, row.Uid, row.Name, row.Avatar, row.Score, row.AchievedAt));
		}

		return entries;
	}

	public async Task<SurfersMyScoreDto?> GetMineAsync(string uid, CancellationToken ct = default)
	{
		var player = await playerRepository.GetEntityByUidAsync(uid, ct);
		if (player is null || await scoreRepository.GetAsync(player.Id, ct) is not { } best)
		{
			return null;
		}

		return new SurfersMyScoreDto(best.Score, await RankOfAsync(best.Score, ct), best.AchievedAt);
	}

	private async Task<int> RankOfAsync(int score, CancellationToken ct) =>
		await scoreRepository.CountHigherThanAsync(score, ct) + 1;

	private void Validate(string uid, SubmitSurfersScoreRequest request)
	{
		var limits = options.Value;
		if (request.Score < 0)
		{
			throw new SurfersValidationException("Score can't be negative.");
		}

		if (!double.IsFinite(request.DurationSeconds) || request.DurationSeconds <= 0 || request.DurationSeconds > limits.MaxRunSeconds)
		{
			throw new SurfersValidationException("Run duration is out of range.");
		}

		if (request.Score > request.DurationSeconds * limits.MaxScorePerSecond)
		{
			// Logged so a too-tight limit (after a game balance change) shows up before players complain.
			logger.LogWarning("Rejected Anime Surfers score {Score} in {DurationSeconds}s from {Uid}", request.Score, request.DurationSeconds, uid);
			throw new SurfersValidationException("That score isn't possible in that time.");
		}
	}
}
