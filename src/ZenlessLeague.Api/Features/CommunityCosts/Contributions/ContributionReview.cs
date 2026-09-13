namespace ZenlessLeague.Api.Features.CommunityCosts.Contributions;

public enum ReviewVote { Positive, Neutral, Negative }

// At most one review per (ContributionId, ReviewerPlayerId) — enforced by a unique index in
// AppDbContext, backing ContributionService.UpsertReviewAsync. A reviewer who already left a
// review edits it in place instead of adding another.
public class ContributionReview
{
	public Guid Id { get; set; }
	public Guid ContributionId { get; set; }
	public Guid ReviewerPlayerId { get; set; }
	public ReviewVote Vote { get; set; }
	public required string Comment { get; set; }
	public DateTimeOffset CreatedAt { get; set; }
	public DateTimeOffset UpdatedAt { get; set; }
}
