using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Data;

namespace ZenlessLeague.Api.Features.CommunityCosts.Contributions;

public interface IContributionRepository
{
	Task<List<Contribution>> ListAsync(CancellationToken ct = default);

	// Returns a tracked entity — callers may mutate it in place (status/cost-row apply on
	// approve) and call SaveChangesAsync.
	Task<Contribution?> GetByIdAsync(Guid id, CancellationToken ct = default);

	Task<Contribution> CreateAsync(Contribution contribution, CancellationToken ct = default);

	// Every review across every contribution — used by the list endpoint to compute each row's
	// score/reviewCount in one query rather than one per contribution.
	Task<List<ContributionReview>> ListAllReviewsAsync(CancellationToken ct = default);

	Task<List<ContributionReview>> ListReviewsForContributionAsync(Guid contributionId, CancellationToken ct = default);
	Task<ContributionReview?> GetReviewAsync(Guid contributionId, Guid reviewerPlayerId, CancellationToken ct = default);
	Task<ContributionReview?> GetReviewByIdAsync(Guid reviewId, CancellationToken ct = default);
	Task<ContributionReview> AddReviewAsync(ContributionReview review, CancellationToken ct = default);

	Task<List<ReviewComment>> ListCommentsAsync(IEnumerable<Guid> reviewIds, CancellationToken ct = default);
	Task<ReviewComment> AddCommentAsync(ReviewComment comment, CancellationToken ct = default);

	Task SaveChangesAsync(CancellationToken ct = default);
}

public class ContributionRepository(AppDbContext context) : IContributionRepository
{
	public Task<List<Contribution>> ListAsync(CancellationToken ct = default) =>
		context.Contributions.OrderByDescending(c => c.CreatedAt).ToListAsync(ct);

	public Task<Contribution?> GetByIdAsync(Guid id, CancellationToken ct = default) =>
		context.Contributions.FirstOrDefaultAsync(c => c.Id == id, ct);

	public async Task<Contribution> CreateAsync(Contribution contribution, CancellationToken ct = default)
	{
		context.Contributions.Add(contribution);
		await context.SaveChangesAsync(ct);
		return contribution;
	}

	public Task<List<ContributionReview>> ListAllReviewsAsync(CancellationToken ct = default) =>
		context.ContributionReviews.ToListAsync(ct);

	public Task<List<ContributionReview>> ListReviewsForContributionAsync(Guid contributionId, CancellationToken ct = default) =>
		context.ContributionReviews
			.Where(r => r.ContributionId == contributionId)
			.OrderBy(r => r.CreatedAt)
			.ToListAsync(ct);

	public Task<ContributionReview?> GetReviewAsync(Guid contributionId, Guid reviewerPlayerId, CancellationToken ct = default) =>
		context.ContributionReviews.FirstOrDefaultAsync(
			r => r.ContributionId == contributionId && r.ReviewerPlayerId == reviewerPlayerId, ct);

	public Task<ContributionReview?> GetReviewByIdAsync(Guid reviewId, CancellationToken ct = default) =>
		context.ContributionReviews.FirstOrDefaultAsync(r => r.Id == reviewId, ct);

	public async Task<ContributionReview> AddReviewAsync(ContributionReview review, CancellationToken ct = default)
	{
		context.ContributionReviews.Add(review);
		await context.SaveChangesAsync(ct);
		return review;
	}

	public Task<List<ReviewComment>> ListCommentsAsync(IEnumerable<Guid> reviewIds, CancellationToken ct = default) =>
		context.ReviewComments
			.Where(c => reviewIds.Contains(c.ReviewId))
			.OrderBy(c => c.CreatedAt)
			.ToListAsync(ct);

	public async Task<ReviewComment> AddCommentAsync(ReviewComment comment, CancellationToken ct = default)
	{
		context.ReviewComments.Add(comment);
		await context.SaveChangesAsync(ct);
		return comment;
	}

	public Task SaveChangesAsync(CancellationToken ct = default) => context.SaveChangesAsync(ct);
}
