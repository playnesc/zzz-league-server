namespace ZenlessLeague.Api.Features.CommunityCosts.Contributions;

public enum ContributionStatus { Pending, Approved, Rejected }

// A community proposal to change a cost value. Exactly one of AgentId/EngineId is set
// (validated in ContributionService, not a DB check constraint — this codebase doesn't use
// those). EngineId set + OverrideAgentId null = a proposal for that engine's own base cost;
// EngineId set + OverrideAgentId set = a per-agent override proposal. Approving an
// OverrideAgentId proposal for a (engine, agent) pair with no existing EngineCostRow is the
// entire "assign this agent to this engine" mechanism — no separate assignment concept exists.
public class Contribution
{
	// Cap for the free-text fields of contributions, reviews and review comments.
	public const int TextMaxLength = 2000;

	public Guid Id { get; set; }
	public Guid AuthorPlayerId { get; set; }

	public string? AgentId { get; set; }
	public string? EngineId { get; set; }
	public string? OverrideAgentId { get; set; }

	public required string Message { get; set; }

	// Length 7 (M0-M6) for an agent contribution, 6 (R1-R5 + off) for an engine-base
	// contribution, 5 (R1-R5) for an engine-agent-override contribution.
	public required int[] ProposedCosts { get; set; }

	public ContributionStatus Status { get; set; } = ContributionStatus.Pending;
	public DateTimeOffset CreatedAt { get; set; }

	public Guid? ReviewedByPlayerId { get; set; }
	public DateTimeOffset? ReviewedAt { get; set; }
}
