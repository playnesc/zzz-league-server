using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ZenlessLeague.Api.Data;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Seasons;
using ZenlessLeague.Api.Features.Tournaments;
using ZenlessLeague.Api.Features.Tournaments.Matches;

namespace ZenlessLeague.Migrator;

public class MigrationReport
{
	public int Players { get; set; }
	public int Archives { get; set; }
	public int Tournaments { get; set; }
	public int ChallongeParticipants { get; set; }
	public int Registrations { get; set; }
	public int Matches { get; set; }
	public int HistoryEntries { get; set; }

	// 0 or 1 (singleton row) -- 1 means RTDB's "timer" path had a value and it was written.
	public int SeasonTimer { get; set; }
}

// Reads each RTDB subtree once and upserts into Postgres, in FK-dependency order: Players (must
// come first — everything else FKs into it) -> Archives (independent) -> Tournaments, with their
// nested ChallongeParticipants/Registrations/Matches migrated inline -> HistoryEntries (top-level
// historyV3, dropping the historyByPlayer fan-out) -> a final pass linking TournamentMatch.HistoryEntryId
// (legacy's runtime-only `historyKey`) now that both sides exist.
//
// Idempotent: every upsert is keyed by the legacy RTDB id (Tournament.LegacyId,
// HistoryEntry.LegacyHistoryKey, or the entity's own natural key for Players/Registrations/Matches/
// ChallongeParticipants), so re-running against a partially-migrated database is safe.
//
// Dry-run wraps the whole run in a database transaction and rolls back instead of committing —
// this makes dry-run a completely faithful simulation (every query sees this run's own writes,
// same as a live run) rather than special-cased no-op logic scattered through each phase.
public class MigrationRunner(AppDbContext db, IRtdbReader rtdb, bool dryRun)
{
	private HashSet<string> _playerUids = [];
	private Dictionary<string, Guid> _playerIdsByUid = [];

	public async Task<MigrationReport> RunAsync(CancellationToken ct = default)
	{
		IDbContextTransaction? transaction = db.Database.IsRelational()
			? await db.Database.BeginTransactionAsync(ct)
			: null;

		try
		{
			var report = new MigrationReport
			{
				Players = await MigratePlayersAsync(ct),
				Archives = await MigrateArchivesAsync(ct),
			};

			var (tournaments, participants, registrations, matches, matchHistoryLinks) = await MigrateTournamentsAsync(ct);
			report.Tournaments = tournaments;
			report.ChallongeParticipants = participants;
			report.Registrations = registrations;
			report.Matches = matches;

			report.HistoryEntries = await MigrateHistoryAsync(ct);
			await LinkMatchHistoryEntriesAsync(matchHistoryLinks, ct);

			report.SeasonTimer = await MigrateSeasonTimerAsync(ct);

			if (transaction is not null)
			{
				if (dryRun)
				{
					await transaction.RollbackAsync(ct);
				}
				else
				{
					await transaction.CommitAsync(ct);
				}
			}

			return report;
		}
		finally
		{
			if (transaction is not null)
			{
				await transaction.DisposeAsync();
			}
		}
	}

	private async Task<int> MigratePlayersAsync(CancellationToken ct)
	{
		var players = await rtdb.GetMapAsync("players", ct);
		var count = 0;

		if (players is not null)
		{
			foreach (var (uid, element) in players)
			{
				var legacy = RtdbReader.Parse<LegacyPlayer>(element);
				if (legacy is null)
				{
					continue;
				}

				var player = await db.Players.FirstOrDefaultAsync(p => p.Uid == uid, ct);
				if (player is null)
				{
					player = new Player { Id = Guid.NewGuid(), Uid = uid, Name = legacy.Name ?? uid, CreatedAt = DateTimeOffset.UtcNow };
					db.Players.Add(player);
				}

				// A player can delete/recreate their Firebase account and re-link the same Discord
				// to the new uid — the old uid then drops out of RTDB entirely, but its stale
				// Postgres row (from a prior run) still holds the DiscordId and would otherwise
				// violate the unique constraint. RTDB is the source of truth being migrated, so the
				// live claim wins: clear the stale row's DiscordId first.
				if (!string.IsNullOrEmpty(legacy.DiscordId))
				{
					var staleOwner = await db.Players
						.Where(p => p.DiscordId == legacy.DiscordId && p.Uid != uid)
						.FirstOrDefaultAsync(ct);
					if (staleOwner is not null)
					{
						Console.WriteLine($"  [warn] DiscordId {legacy.DiscordId} moved from uid {staleOwner.Uid} to {uid} — clearing stale link");
						staleOwner.DiscordId = null;
					}
				}

				LegacyMapper.ApplyTo(player, uid, legacy);
				count++;
			}

			await db.SaveChangesAsync(ct);
		}

		// Preloaded for fast existence checks/FK resolution in later phases — includes players
		// already in the database from a prior run, not just ones just migrated, so re-runs
		// resolve refs correctly.
		_playerIdsByUid = await db.Players.ToDictionaryAsync(p => p.Uid, p => p.Id, ct);
		_playerUids = _playerIdsByUid.Keys.ToHashSet();

		return count;
	}

	private async Task<int> MigrateArchivesAsync(CancellationToken ct)
	{
		var archives = await rtdb.GetMapAsync("archives", ct);
		if (archives is null)
		{
			return 0;
		}

		var count = 0;
		foreach (var (seasonName, element) in archives)
		{
			var legacyPlayers = RtdbReader.Parse<List<LegacyArchivePlayer>>(element) ?? [];

			// Re-running with a season name that already exists overwrites the snapshot — same
			// delete-then-recreate approach as SeasonService.ResetSeasonAsync, for the same reason
			// (avoids an EF change-tracking hazard around clearing a required child collection).
			var existing = await db.Archives.Include(a => a.Players).FirstOrDefaultAsync(a => a.SeasonName == seasonName, ct);
			if (existing is not null)
			{
				db.Archives.Remove(existing);
			}

			db.Archives.Add(new Archive
			{
				Id = Guid.NewGuid(),
				SeasonName = seasonName,
				CreatedAt = DateTimeOffset.UtcNow,
				Players = legacyPlayers.Select(LegacyMapper.ToArchivedPlayerSnapshot).ToList(),
			});
			count++;
		}

		await db.SaveChangesAsync(ct);
		return count;
	}

	private async Task<(int Tournaments, int Participants, int Registrations, int Matches, List<(Guid MatchId, string LegacyHistoryKey)> MatchHistoryLinks)>
		MigrateTournamentsAsync(CancellationToken ct)
	{
		var tournaments = await rtdb.GetMapAsync("tournaments", ct);
		var matchHistoryLinks = new List<(Guid, string)>();
		if (tournaments is null)
		{
			return (0, 0, 0, 0, matchHistoryLinks);
		}

		var tournamentCount = 0;
		var participantCount = 0;
		var registrationCount = 0;
		var matchCount = 0;

		foreach (var (legacyId, element) in tournaments)
		{
			var legacy = RtdbReader.Parse<LegacyTournament>(element);
			if (legacy is null)
			{
				continue;
			}

			var tournament = await db.Tournaments.FirstOrDefaultAsync(t => t.LegacyId == legacyId, ct);
			if (tournament is null)
			{
				tournament = new Tournament { Id = Guid.NewGuid(), Name = legacy.Name ?? legacyId };
				db.Tournaments.Add(tournament);
			}

			LegacyMapper.ApplyTo(tournament, legacyId, legacy, _playerIdsByUid);
			tournamentCount++;

			var tournamentId = tournament.Id; // client-generated Guid — valid even before SaveChangesAsync

			if (legacy.ChallongeParticipants is { Count: > 0 })
			{
				var existingParticipantIds = (await db.TournamentChallongeParticipants
					.Where(p => p.TournamentId == tournamentId)
					.Select(p => p.ChallongeParticipantId)
					.ToListAsync(ct)).ToHashSet();

				foreach (var (challongeParticipantId, uid) in legacy.ChallongeParticipants)
				{
					if (existingParticipantIds.Contains(challongeParticipantId) || !_playerUids.Contains(uid))
					{
						continue;
					}

					db.TournamentChallongeParticipants.Add(new TournamentChallongeParticipant
					{
						TournamentId = tournamentId,
						ChallongeParticipantId = challongeParticipantId,
						PlayerId = _playerIdsByUid[uid],
					});
					participantCount++;
				}
			}

			if (legacy.Registrations is { Count: > 0 })
			{
				foreach (var (uid, legacyReg) in legacy.Registrations)
				{
					if (!_playerIdsByUid.TryGetValue(uid, out var playerId))
					{
						continue;
					}

					var registration = await db.TournamentRegistrations
						.FirstOrDefaultAsync(r => r.TournamentId == tournamentId && r.PlayerId == playerId, ct);
					if (registration is null)
					{
						registration = new TournamentRegistration
						{
							Id = Guid.NewGuid(),
							TournamentId = tournamentId,
							PlayerId = playerId,
							Player1Details = LegacyMapper.ToRegistrationDetails(legacyReg),
							RegisteredAt = LegacyMapper.ToDateTimeOffset(legacyReg.RegistrationTimestamp) ?? DateTimeOffset.UtcNow,
							UpdatedAt = DateTimeOffset.UtcNow,
						};
						db.TournamentRegistrations.Add(registration);
					}
					else
					{
						registration.Player1Details = LegacyMapper.ToRegistrationDetails(legacyReg);
						registration.UpdatedAt = DateTimeOffset.UtcNow;
					}

					registration.Approved = legacyReg.Approved ?? false;
					registrationCount++;
				}
			}

			if (legacy.Matches is { Count: > 0 })
			{
				foreach (var (_, legacyMatch) in legacy.Matches)
				{
					var match = await db.TournamentMatches
						.FirstOrDefaultAsync(m => m.TournamentId == tournamentId && m.ChallongeMatchId == legacyMatch.Id, ct);
					if (match is null)
					{
						match = new TournamentMatch
						{
							Id = Guid.NewGuid(),
							TournamentId = tournamentId,
							ChallongeMatchId = legacyMatch.Id,
							State = legacyMatch.State ?? "open",
						};
						db.TournamentMatches.Add(match);
					}

					LegacyMapper.ApplyTo(
						match, tournamentId, legacyMatch,
						LegacyMapper.ResolvePlayerRef(legacyMatch.P1, _playerIdsByUid),
						LegacyMapper.ResolvePlayerRef(legacyMatch.P2, _playerIdsByUid),
						LegacyMapper.ResolvePlayerRef(legacyMatch.WinnerId, _playerIdsByUid),
						LegacyMapper.ResolvePlayerRef(legacyMatch.TechLossUid, _playerIdsByUid));
					matchCount++;

					if (!string.IsNullOrEmpty(legacyMatch.HistoryKey))
					{
						matchHistoryLinks.Add((match.Id, legacyMatch.HistoryKey));
					}
				}
			}

			await db.SaveChangesAsync(ct);
		}

		return (tournamentCount, participantCount, registrationCount, matchCount, matchHistoryLinks);
	}

	private async Task<int> MigrateHistoryAsync(CancellationToken ct)
	{
		var history = await rtdb.GetMapAsync("historyV3", ct);
		if (history is null)
		{
			return 0;
		}

		var tournamentIdMap = await db.Tournaments
			.Where(t => t.LegacyId != null)
			.ToDictionaryAsync(t => t.LegacyId!, t => t.Id, ct);

		var count = 0;
		foreach (var (legacyKey, element) in history)
		{
			var legacy = RtdbReader.Parse<LegacyHistoryEntry>(element);
			if (legacy?.P1 is null || !_playerIdsByUid.TryGetValue(legacy.P1, out var p1PlayerId))
			{
				continue;
			}

			var entry = await db.HistoryEntries.FirstOrDefaultAsync(h => h.LegacyHistoryKey == legacyKey, ct);
			if (entry is null)
			{
				entry = new HistoryEntry { Id = Guid.NewGuid(), P1PlayerId = p1PlayerId, LegacyHistoryKey = legacyKey };
				db.HistoryEntries.Add(entry);
			}

			var (challongeMatchId, kind) = LegacyMapper.ResolveTournamentMatch(legacy.TournamentMatch);
			var tournamentId = legacy.TournamentId is not null && tournamentIdMap.TryGetValue(legacy.TournamentId, out var tid) ? (Guid?)tid : null;

			entry.P1PlayerId = p1PlayerId;
			entry.P1Change = legacy.P1Change;
			entry.P2PlayerId = legacy.P2 is not null && _playerIdsByUid.TryGetValue(legacy.P2, out var p2PlayerId) ? p2PlayerId : null;
			entry.P2Change = legacy.P2Change;
			entry.TournamentId = tournamentId;
			entry.Kind = kind;
			entry.ResultP1 = legacy.ResultP1;
			entry.ResultP2 = legacy.ResultP2;
			entry.Timestamp = LegacyMapper.ToDateTimeOffset(legacy.Timestamp) ?? DateTimeOffset.UtcNow;

			if (challongeMatchId is not null && tournamentId is not null)
			{
				var match = await db.TournamentMatches.FirstOrDefaultAsync(
					m => m.TournamentId == tournamentId && m.ChallongeMatchId == challongeMatchId, ct);
				entry.TournamentMatchId = match?.Id;
			}

			count++;
		}

		await db.SaveChangesAsync(ct);
		return count;
	}

	// RTDB's "timer" path is a bare unix-millis number (functions/admin/setTimer.js just did
	// db.ref("timer").set(timer)), not an object-map node like the rest of this file's sources —
	// same singleton find-or-create as SeasonTimerRepository.SetEndTimestampAsync.
	private async Task<int> MigrateSeasonTimerAsync(CancellationToken ct)
	{
		var timerMillis = await rtdb.GetValueAsync<long?>("timer", ct);
		if (timerMillis is null)
		{
			return 0;
		}

		var endTimestamp = LegacyMapper.ToDateTimeOffset(timerMillis);
		var timer = await db.SeasonTimers.FindAsync([SeasonTimer.SingletonId], ct);
		if (timer is null)
		{
			db.SeasonTimers.Add(new SeasonTimer { EndTimestamp = endTimestamp });
		}
		else
		{
			timer.EndTimestamp = endTimestamp;
		}

		await db.SaveChangesAsync(ct);
		return 1;
	}

	private async Task LinkMatchHistoryEntriesAsync(List<(Guid MatchId, string LegacyHistoryKey)> links, CancellationToken ct)
	{
		if (links.Count == 0)
		{
			return;
		}

		foreach (var (matchId, legacyHistoryKey) in links)
		{
			var entry = await db.HistoryEntries.FirstOrDefaultAsync(h => h.LegacyHistoryKey == legacyHistoryKey, ct);
			if (entry is null)
			{
				continue;
			}

			var match = await db.TournamentMatches.FirstOrDefaultAsync(m => m.Id == matchId, ct);
			if (match is not null)
			{
				match.HistoryEntryId = entry.Id;
			}
		}

		await db.SaveChangesAsync(ct);
	}
}
