namespace ZenlessLeague.Api.Features.CommunityCosts.Contributions;

public record CreateAgentContributionRequest(string AgentId, string Message, int[] ProposedCosts);

public record CreateEngineContributionRequest(string EngineId, string? OverrideAgentId, string Message, int[] ProposedCosts);

public record UpsertReviewRequest(ReviewVote Vote, string Comment);

public record AddReviewCommentRequest(string Text);

// Lean — no nested reviews, just enough to drive the row alert-dot / tab-badge / log-card UI.
// Score is 0..1, computed fresh on every read (see ContributionService.ComputeScore).
public record ContributionListItemDto(
	Guid Id,
	string? AgentId,
	string? EngineId,
	string? OverrideAgentId,
	string AuthorName,
	string Message,
	int[] ProposedCosts,
	ContributionStatus Status,
	DateTimeOffset CreatedAt,
	int ReviewCount,
	double Score);

public record ReviewCommentDto(Guid Id, string AuthorName, string Text, DateTimeOffset CreatedAt);

public record ReviewDto(
	Guid Id,
	string ReviewerName,
	ReviewVote Vote,
	string Comment,
	DateTimeOffset CreatedAt,
	DateTimeOffset UpdatedAt,
	List<ReviewCommentDto> Comments);

public record ContributionDetailDto(
	Guid Id,
	string? AgentId,
	string? EngineId,
	string? OverrideAgentId,
	string AuthorName,
	string Message,
	int[] ProposedCosts,
	ContributionStatus Status,
	DateTimeOffset CreatedAt,
	double Score,
	List<ReviewDto> Reviews);
