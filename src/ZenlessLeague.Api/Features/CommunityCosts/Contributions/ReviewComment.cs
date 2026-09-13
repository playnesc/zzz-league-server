namespace ZenlessLeague.Api.Features.CommunityCosts.Contributions;

// A discussion reply on a single ContributionReview. Unrestricted — any number per review,
// from anyone, no upsert (unlike ContributionReview itself).
public class ReviewComment
{
	public Guid Id { get; set; }
	public Guid ReviewId { get; set; }
	public Guid AuthorPlayerId { get; set; }
	public required string Text { get; set; }
	public DateTimeOffset CreatedAt { get; set; }
}
