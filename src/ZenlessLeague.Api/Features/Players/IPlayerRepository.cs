namespace ZenlessLeague.Api.Features.Players;

public interface IPlayerRepository
{
	Task<PlayerSummary?> GetByUidAsync(string uid, CancellationToken ct = default);
	Task<PlayerSummary?> GetByIdAsync(Guid id, CancellationToken ct = default);

	// Returns a tracked entity — callers may mutate it in place and call SaveChangesAsync.
	Task<Player?> GetEntityByUidAsync(string uid, CancellationToken ct = default);
	Task<Player?> GetEntityByIdAsync(Guid id, CancellationToken ct = default);
	Task<Player?> GetByNameAsync(string name, CancellationToken ct = default);
	Task<Player?> GetEntityByDiscordIdAsync(string discordId, CancellationToken ct = default);
	Task<List<Player>> ListAsync(CancellationToken ct = default);
	Task<Player> CreateAsync(Player player, CancellationToken ct = default);
	Task<bool> DeleteByUidAsync(string uid, CancellationToken ct = default);

	// Batch Id -> Uid resolution for DTO mappers translating internal Guid FKs back to the
	// Firebase uid strings the frontend expects — one query per list, not one per row.
	Task<Dictionary<Guid, string>> GetUidsByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct = default);

	// The reverse of the above — batch Uid -> Id resolution for request payloads that carry a
	// list of Firebase uids (e.g. SplitTournamentRequest.DivisionGroups) needing translation to
	// internal Guid FKs before comparison/lookup.
	Task<Dictionary<string, Guid>> GetIdsByUidsAsync(IEnumerable<string> uids, CancellationToken ct = default);

	Task SaveChangesAsync(CancellationToken ct = default);
}

public record PlayerSummary(string Uid, string Name, string? Avatar, bool IsMidConfirmed, bool IsHighConfirmed);
