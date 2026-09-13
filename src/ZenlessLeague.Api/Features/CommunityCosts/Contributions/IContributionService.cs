using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.CommunityCosts.Agents;
using ZenlessLeague.Api.Features.CommunityCosts.Engines;
using ZenlessLeague.Api.Features.Players;

namespace ZenlessLeague.Api.Features.CommunityCosts.Contributions;

public interface IContributionService
{
	Task<List<ContributionListItemDto>> ListAsync(CancellationToken ct = default);
	Task<ContributionDetailDto?> GetAsync(Guid id, CancellationToken ct = default);
	Task<ContributionDetailDto> CreateAgentContributionAsync(string authorUid, CreateAgentContributionRequest request, CancellationToken ct = default);
	Task<ContributionDetailDto> CreateEngineContributionAsync(string authorUid, CreateEngineContributionRequest request, CancellationToken ct = default);
	Task<ContributionDetailDto?> UpsertReviewAsync(Guid contributionId, string reviewerUid, UpsertReviewRequest request, CancellationToken ct = default);
	Task<ContributionDetailDto?> AddCommentAsync(Guid contributionId, Guid reviewId, string authorUid, AddReviewCommentRequest request, CancellationToken ct = default);
	Task<ContributionDetailDto?> ApproveAsync(Guid id, string moderatorUid, CancellationToken ct = default);
	Task<ContributionDetailDto?> RejectAsync(Guid id, string moderatorUid, CancellationToken ct = default);
}

/// <summary>Thrown for cross-store validation failures (missing agent/engine/player, malformed proposal) — maps to 400.</summary>
public class ContributionValidationException(string message) : ValidationApiException(message);

public class ContributionService(
	IContributionRepository contributionRepository,
	IAgentCostRepository agentCostRepository,
	IEngineCostRepository engineCostRepository,
	IPlayerRepository playerRepository) : IContributionService
{
	// 0 (no reviews) to 1 (5+ positive reviews, none diluted by negative/neutral). Ported
	// verbatim from the frontend mock's contributionScore() — see mockContributions.ts.
	public static double ComputeScore(IReadOnlyCollection<ContributionReview> reviews)
	{
		if (reviews.Count == 0)
		{
			return 0;
		}

		var positive = reviews.Count(r => r.Vote == ReviewVote.Positive);
		var ratio = (double)positive / reviews.Count;
		var progress = Math.Min(positive, 5) / 5.0;
		return ratio * progress;
	}

	public async Task<List<ContributionListItemDto>> ListAsync(CancellationToken ct = default)
	{
		var contributions = await contributionRepository.ListAsync(ct);
		var reviewsByContribution = (await contributionRepository.ListAllReviewsAsync(ct))
			.GroupBy(r => r.ContributionId)
			.ToDictionary(g => g.Key, g => g.ToList());

		var names = new PlayerNameCache(playerRepository);
		var dtos = new List<ContributionListItemDto>(contributions.Count);
		foreach (var c in contributions)
		{
			reviewsByContribution.TryGetValue(c.Id, out var reviews);
			reviews ??= [];
			dtos.Add(new ContributionListItemDto(
				c.Id, c.AgentId, c.EngineId, c.OverrideAgentId,
				await names.GetAsync(c.AuthorPlayerId, ct),
				c.Message, c.ProposedCosts, c.Status, c.CreatedAt,
				reviews.Count, ComputeScore(reviews)));
		}

		return dtos;
	}

	public async Task<ContributionDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
	{
		var contribution = await contributionRepository.GetByIdAsync(id, ct);
		return contribution is null ? null : await ToDetailDtoAsync(contribution, ct);
	}

	public async Task<ContributionDetailDto> CreateAgentContributionAsync(
		string authorUid, CreateAgentContributionRequest request, CancellationToken ct = default)
	{
		var author = await playerRepository.GetEntityByUidAsync(authorUid, ct)
			?? throw new ContributionValidationException($"Player '{authorUid}' does not exist.");
		_ = await agentCostRepository.GetCatalogEntryAsync(request.AgentId, ct)
			?? throw new ContributionValidationException($"Agent '{request.AgentId}' does not exist.");

		if (request.ProposedCosts.Length != 7)
		{
			throw new ContributionValidationException("An agent cost proposal must have exactly 7 values (M0-M6).");
		}

		var contribution = await contributionRepository.CreateAsync(new Contribution
		{
			Id = Guid.NewGuid(),
			AuthorPlayerId = author.Id,
			AgentId = request.AgentId,
			Message = request.Message,
			ProposedCosts = request.ProposedCosts,
			Status = ContributionStatus.Pending,
			CreatedAt = DateTimeOffset.UtcNow,
		}, ct);

		return (await ToDetailDtoAsync(contribution, ct));
	}

	public async Task<ContributionDetailDto> CreateEngineContributionAsync(
		string authorUid, CreateEngineContributionRequest request, CancellationToken ct = default)
	{
		var author = await playerRepository.GetEntityByUidAsync(authorUid, ct)
			?? throw new ContributionValidationException($"Player '{authorUid}' does not exist.");
		_ = await engineCostRepository.GetCatalogEntryAsync(request.EngineId, ct)
			?? throw new ContributionValidationException($"Engine '{request.EngineId}' does not exist.");

		if (request.OverrideAgentId is not null)
		{
			_ = await agentCostRepository.GetCatalogEntryAsync(request.OverrideAgentId, ct)
				?? throw new ContributionValidationException($"Agent '{request.OverrideAgentId}' does not exist.");
			if (request.ProposedCosts.Length != 5)
			{
				throw new ContributionValidationException("A per-agent engine cost proposal must have exactly 5 values (R1-R5).");
			}
		}
		else if (request.ProposedCosts.Length != 6)
		{
			throw new ContributionValidationException("An engine base cost proposal must have exactly 6 values.");
		}

		var contribution = await contributionRepository.CreateAsync(new Contribution
		{
			Id = Guid.NewGuid(),
			AuthorPlayerId = author.Id,
			EngineId = request.EngineId,
			OverrideAgentId = request.OverrideAgentId,
			Message = request.Message,
			ProposedCosts = request.ProposedCosts,
			Status = ContributionStatus.Pending,
			CreatedAt = DateTimeOffset.UtcNow,
		}, ct);

		return (await ToDetailDtoAsync(contribution, ct));
	}

	public async Task<ContributionDetailDto?> UpsertReviewAsync(
		Guid contributionId, string reviewerUid, UpsertReviewRequest request, CancellationToken ct = default)
	{
		var contribution = await contributionRepository.GetByIdAsync(contributionId, ct);
		if (contribution is null)
		{
			return null;
		}

		var reviewer = await playerRepository.GetEntityByUidAsync(reviewerUid, ct)
			?? throw new ContributionValidationException($"Player '{reviewerUid}' does not exist.");

		var existing = await contributionRepository.GetReviewAsync(contributionId, reviewer.Id, ct);
		if (existing is not null)
		{
			existing.Vote = request.Vote;
			existing.Comment = request.Comment;
			existing.UpdatedAt = DateTimeOffset.UtcNow;
			await contributionRepository.SaveChangesAsync(ct);
		}
		else
		{
			await contributionRepository.AddReviewAsync(new ContributionReview
			{
				Id = Guid.NewGuid(),
				ContributionId = contributionId,
				ReviewerPlayerId = reviewer.Id,
				Vote = request.Vote,
				Comment = request.Comment,
				CreatedAt = DateTimeOffset.UtcNow,
				UpdatedAt = DateTimeOffset.UtcNow,
			}, ct);
		}

		return await ToDetailDtoAsync(contribution, ct);
	}

	public async Task<ContributionDetailDto?> AddCommentAsync(
		Guid contributionId, Guid reviewId, string authorUid, AddReviewCommentRequest request, CancellationToken ct = default)
	{
		var contribution = await contributionRepository.GetByIdAsync(contributionId, ct);
		if (contribution is null)
		{
			return null;
		}

		var review = await contributionRepository.GetReviewByIdAsync(reviewId, ct);
		if (review is null || review.ContributionId != contributionId)
		{
			throw new ContributionValidationException($"Review '{reviewId}' does not exist on this contribution.");
		}

		var author = await playerRepository.GetEntityByUidAsync(authorUid, ct)
			?? throw new ContributionValidationException($"Player '{authorUid}' does not exist.");

		await contributionRepository.AddCommentAsync(new ReviewComment
		{
			Id = Guid.NewGuid(),
			ReviewId = reviewId,
			AuthorPlayerId = author.Id,
			Text = request.Text,
			CreatedAt = DateTimeOffset.UtcNow,
		}, ct);

		return await ToDetailDtoAsync(contribution, ct);
	}

	public Task<ContributionDetailDto?> ApproveAsync(Guid id, string moderatorUid, CancellationToken ct = default) =>
		ResolveAsync(id, moderatorUid, ContributionStatus.Approved, ct);

	public Task<ContributionDetailDto?> RejectAsync(Guid id, string moderatorUid, CancellationToken ct = default) =>
		ResolveAsync(id, moderatorUid, ContributionStatus.Rejected, ct);

	private async Task<ContributionDetailDto?> ResolveAsync(
		Guid id, string moderatorUid, ContributionStatus resolution, CancellationToken ct)
	{
		var contribution = await contributionRepository.GetByIdAsync(id, ct);
		if (contribution is null)
		{
			return null;
		}

		if (contribution.Status != ContributionStatus.Pending)
		{
			throw new ContributionValidationException("This contribution has already been reviewed.");
		}

		var moderator = await playerRepository.GetEntityByUidAsync(moderatorUid, ct)
			?? throw new ContributionValidationException($"Player '{moderatorUid}' does not exist.");

		if (resolution == ContributionStatus.Approved)
		{
			await ApplyCostAsync(contribution, ct);
		}

		contribution.Status = resolution;
		contribution.ReviewedByPlayerId = moderator.Id;
		contribution.ReviewedAt = DateTimeOffset.UtcNow;
		await contributionRepository.SaveChangesAsync(ct);

		return await ToDetailDtoAsync(contribution, ct);
	}

	// The entire "assign agent to engine" mechanism from the plan: an engine-override
	// contribution for a (engine, agent) pair with no existing EngineCostRow creates one on
	// approve, rather than requiring a separate assignment step.
	private async Task ApplyCostAsync(Contribution contribution, CancellationToken ct)
	{
		if (contribution.AgentId is not null)
		{
			var row = await agentCostRepository.GetCostRowAsync(contribution.AgentId, null, ct);
			if (row is null)
			{
				await agentCostRepository.CreateCostRowAsync(new AgentCostRow
				{
					Id = Guid.NewGuid(),
					AgentId = contribution.AgentId,
					PotentialTier = null,
					Costs = contribution.ProposedCosts,
					UpdatedAt = DateTimeOffset.UtcNow,
				}, ct);
			}
			else
			{
				row.Costs = contribution.ProposedCosts;
				row.UpdatedAt = DateTimeOffset.UtcNow;
			}

			return;
		}

		if (contribution.EngineId is null)
		{
			return;
		}

		var engineRow = await engineCostRepository.GetCostRowAsync(contribution.EngineId, contribution.OverrideAgentId, ct);

		// A base proposal always carries all 6 values. An override proposal only carries R1-R5
		// (5 values) — the 6th "off" value isn't part of that flow, so preserve whatever the
		// row already had for it (0 for a brand-new row, matching every observed override row
		// in the real NESC export).
		var costs = contribution.ProposedCosts.Length == 6
			? contribution.ProposedCosts
			: [.. contribution.ProposedCosts, engineRow?.Costs.ElementAtOrDefault(5) ?? 0];

		if (engineRow is null)
		{
			await engineCostRepository.CreateCostRowAsync(new EngineCostRow
			{
				Id = Guid.NewGuid(),
				EngineId = contribution.EngineId,
				AgentId = contribution.OverrideAgentId,
				Costs = costs,
				UpdatedAt = DateTimeOffset.UtcNow,
			}, ct);
		}
		else
		{
			engineRow.Costs = costs;
			engineRow.UpdatedAt = DateTimeOffset.UtcNow;
		}
	}

	private async Task<ContributionDetailDto> ToDetailDtoAsync(Contribution contribution, CancellationToken ct)
	{
		var reviews = await contributionRepository.ListReviewsForContributionAsync(contribution.Id, ct);
		var comments = reviews.Count == 0
			? []
			: await contributionRepository.ListCommentsAsync(reviews.Select(r => r.Id), ct);
		var commentsByReview = comments.GroupBy(c => c.ReviewId).ToDictionary(g => g.Key, g => g.ToList());

		var names = new PlayerNameCache(playerRepository);
		var reviewDtos = new List<ReviewDto>(reviews.Count);
		foreach (var r in reviews)
		{
			commentsByReview.TryGetValue(r.Id, out var reviewComments);
			reviewComments ??= [];
			var commentDtos = new List<ReviewCommentDto>(reviewComments.Count);
			foreach (var c in reviewComments)
			{
				commentDtos.Add(new ReviewCommentDto(c.Id, await names.GetAsync(c.AuthorPlayerId, ct), c.Text, c.CreatedAt));
			}

			reviewDtos.Add(new ReviewDto(
				r.Id, await names.GetAsync(r.ReviewerPlayerId, ct), r.Vote, r.Comment, r.CreatedAt, r.UpdatedAt, commentDtos));
		}

		return new ContributionDetailDto(
			contribution.Id, contribution.AgentId, contribution.EngineId, contribution.OverrideAgentId,
			await names.GetAsync(contribution.AuthorPlayerId, ct),
			contribution.Message, contribution.ProposedCosts, contribution.Status, contribution.CreatedAt,
			ComputeScore(reviews), reviewDtos);
	}

	// Per-request memoization — the same author/reviewer/commenter id often repeats across a
	// contribution's reviews and comments, so this avoids re-querying Players for each one.
	private sealed class PlayerNameCache(IPlayerRepository playerRepository)
	{
		private readonly Dictionary<Guid, string> cache = [];

		public async Task<string> GetAsync(Guid playerId, CancellationToken ct)
		{
			if (cache.TryGetValue(playerId, out var name))
			{
				return name;
			}

			var player = await playerRepository.GetByIdAsync(playerId, ct);
			name = player?.Name ?? "Unknown";
			cache[playerId] = name;
			return name;
		}
	}
}
