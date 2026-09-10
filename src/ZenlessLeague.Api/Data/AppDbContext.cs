using Microsoft.EntityFrameworkCore;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Seasons;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments;
using ZenlessLeague.Api.Features.Tournaments.Matches;

namespace ZenlessLeague.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
	public DbSet<Player> Players => Set<Player>();
	public DbSet<Team> Teams => Set<Team>();
	public DbSet<Tournament> Tournaments => Set<Tournament>();
	public DbSet<TournamentRegistration> TournamentRegistrations => Set<TournamentRegistration>();
	public DbSet<HistoryEntry> HistoryEntries => Set<HistoryEntry>();
	public DbSet<TournamentMatch> TournamentMatches => Set<TournamentMatch>();
	public DbSet<TournamentChallongeParticipant> TournamentChallongeParticipants => Set<TournamentChallongeParticipant>();
	public DbSet<Archive> Archives => Set<Archive>();
	public DbSet<ArchivedPlayerSnapshot> ArchivedPlayerSnapshots => Set<ArchivedPlayerSnapshot>();
	public DbSet<SeasonTimer> SeasonTimers => Set<SeasonTimer>();

	protected override void OnModelCreating(ModelBuilder modelBuilder)
	{
		modelBuilder.Entity<Player>(player =>
		{
			player.HasKey(p => p.Id);
			player.Property(p => p.Uid).IsRequired().HasMaxLength(128);
			player.HasIndex(p => p.Uid).IsUnique();
			player.Property(p => p.Name).IsRequired().HasMaxLength(100);
			player.HasIndex(p => p.Name).IsUnique();
			player.HasIndex(p => p.DiscordId).IsUnique().HasFilter("\"DiscordId\" IS NOT NULL");

			player.OwnsOne(p => p.LastRegistrationCache, d =>
			{
				d.Property(x => x.GameUid).HasColumnName("LastRegistration_GameUid");
				d.Property(x => x.PrizeUid).HasColumnName("LastRegistration_PrizeUid");
				d.Property(x => x.PrizeAsMoney).HasColumnName("LastRegistration_PrizeAsMoney");
				d.Property(x => x.DarteNickname).HasColumnName("LastRegistration_DarteNickname");
				d.Property(x => x.DartePresetName).HasColumnName("LastRegistration_DartePresetName");
				d.Property(x => x.RosterName).HasColumnName("LastRegistration_RosterName");
				d.Property(x => x.RosterScreenshotUrl).HasColumnName("LastRegistration_RosterScreenshotUrl");
				d.Property(x => x.HoyolabScreenshotUrl).HasColumnName("LastRegistration_HoyolabScreenshotUrl");
			});
		});

		modelBuilder.Entity<Team>(team =>
		{
			team.Property(t => t.Name).IsRequired().HasMaxLength(100);
			team.HasOne<Player>().WithMany().HasForeignKey(t => t.Player1Id).OnDelete(DeleteBehavior.Restrict);
			team.HasOne<Player>().WithMany().HasForeignKey(t => t.Player2Id).OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<Tournament>(tournament =>
		{
			tournament.Property(t => t.Name).IsRequired().HasMaxLength(200);
			tournament.HasIndex(t => t.LegacyId).IsUnique().HasFilter("\"LegacyId\" IS NOT NULL");
			tournament.HasOne<Player>().WithMany().HasForeignKey(t => t.WinnerId).OnDelete(DeleteBehavior.Restrict);
			tournament.HasOne<Team>().WithMany().HasForeignKey(t => t.WinnerTeamId).OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<TournamentRegistration>(reg =>
		{
			reg.OwnsOne(r => r.Player1Details, d =>
			{
				d.Property(p => p.GameUid).HasColumnName("Player1_GameUid");
				d.Property(p => p.PrizeUid).HasColumnName("Player1_PrizeUid");
				d.Property(p => p.PrizeAsMoney).HasColumnName("Player1_PrizeAsMoney");
				d.Property(p => p.DarteNickname).HasColumnName("Player1_DarteNickname");
				d.Property(p => p.DartePresetName).HasColumnName("Player1_DartePresetName");
				d.Property(p => p.RosterName).HasColumnName("Player1_RosterName");
				d.Property(p => p.RosterScreenshotUrl).HasColumnName("Player1_RosterScreenshotUrl");
				d.Property(p => p.HoyolabScreenshotUrl).HasColumnName("Player1_HoyolabScreenshotUrl");
			});
			reg.Navigation(r => r.Player1Details).IsRequired();

			reg.OwnsOne(r => r.Player2Details, d =>
			{
				d.Property(p => p.GameUid).HasColumnName("Player2_GameUid");
				d.Property(p => p.PrizeUid).HasColumnName("Player2_PrizeUid");
				d.Property(p => p.PrizeAsMoney).HasColumnName("Player2_PrizeAsMoney");
				d.Property(p => p.DarteNickname).HasColumnName("Player2_DarteNickname");
				d.Property(p => p.DartePresetName).HasColumnName("Player2_DartePresetName");
				d.Property(p => p.RosterName).HasColumnName("Player2_RosterName");
				d.Property(p => p.RosterScreenshotUrl).HasColumnName("Player2_RosterScreenshotUrl");
				d.Property(p => p.HoyolabScreenshotUrl).HasColumnName("Player2_HoyolabScreenshotUrl");
			});

			// A registration is Solo XOR Team — one of PlayerId/TeamId is set, never both.
			// Filtered unique indexes so re-registering the same player/team updates the
			// existing row instead of creating a duplicate.
			reg.HasIndex(r => new { r.TournamentId, r.PlayerId }).IsUnique().HasFilter("\"PlayerId\" IS NOT NULL");
			reg.HasIndex(r => new { r.TournamentId, r.TeamId }).IsUnique().HasFilter("\"TeamId\" IS NOT NULL");
			reg.HasOne<Player>().WithMany().HasForeignKey(r => r.PlayerId).OnDelete(DeleteBehavior.Restrict);
			// Registrations are owned by their tournament — deleting a tournament (any state,
			// mirrors legacy deleteTournament.js removing the whole RTDB subtree) removes them too.
			reg.HasOne<Tournament>().WithMany().HasForeignKey(r => r.TournamentId).OnDelete(DeleteBehavior.Cascade);
			// Player1Details/Player2Details are a snapshot captured at registration time (not a
			// live join), so a deleted team doesn't corrupt historical registration data — just
			// null out the now-meaningless reference, same rationale as HistoryEntry below.
			reg.HasOne<Team>().WithMany().HasForeignKey(r => r.TeamId).OnDelete(DeleteBehavior.SetNull);
		});

		modelBuilder.Entity<TournamentChallongeParticipant>(participant =>
		{
			participant.HasKey(p => new { p.TournamentId, p.ChallongeParticipantId });
			// PlayerId (solo) XOR TeamId (team) — Postgres unique indexes already treat NULLs as
			// distinct from each other, so both stay correct without an explicit filter even
			// though every team-tournament row leaves PlayerId null (and vice versa).
			participant.HasIndex(p => new { p.TournamentId, p.PlayerId }).IsUnique();
			participant.HasIndex(p => new { p.TournamentId, p.TeamId }).IsUnique();
			participant.HasOne<Tournament>().WithMany().HasForeignKey(p => p.TournamentId).OnDelete(DeleteBehavior.Cascade);
			participant.HasOne<Player>().WithMany().HasForeignKey(p => p.PlayerId).OnDelete(DeleteBehavior.Restrict);
			participant.HasOne<Team>().WithMany().HasForeignKey(p => p.TeamId).OnDelete(DeleteBehavior.Restrict);
		});

		modelBuilder.Entity<TournamentMatch>(match =>
		{
			match.HasIndex(m => new { m.TournamentId, m.ChallongeMatchId }).IsUnique();
			match.HasOne<Tournament>().WithMany().HasForeignKey(m => m.TournamentId).OnDelete(DeleteBehavior.Cascade);
			match.HasOne<Player>().WithMany().HasForeignKey(m => m.P1PlayerId).OnDelete(DeleteBehavior.Restrict);
			match.HasOne<Player>().WithMany().HasForeignKey(m => m.P2PlayerId).OnDelete(DeleteBehavior.Restrict);
			match.HasOne<Player>().WithMany().HasForeignKey(m => m.WinnerPlayerId).OnDelete(DeleteBehavior.Restrict);
			match.HasOne<Player>().WithMany().HasForeignKey(m => m.TechLossPlayerId).OnDelete(DeleteBehavior.Restrict);
			match.HasOne<Team>().WithMany().HasForeignKey(m => m.P1TeamId).OnDelete(DeleteBehavior.Restrict);
			match.HasOne<Team>().WithMany().HasForeignKey(m => m.P2TeamId).OnDelete(DeleteBehavior.Restrict);
			match.HasOne<Team>().WithMany().HasForeignKey(m => m.WinnerTeamId).OnDelete(DeleteBehavior.Restrict);
			match.HasOne<Team>().WithMany().HasForeignKey(m => m.TechLossTeamId).OnDelete(DeleteBehavior.Restrict);
			match.HasOne<HistoryEntry>().WithMany().HasForeignKey(m => m.HistoryEntryId).OnDelete(DeleteBehavior.SetNull);
			match.HasOne<HistoryEntry>().WithMany().HasForeignKey(m => m.HistoryEntryId2).OnDelete(DeleteBehavior.SetNull);
		});

		modelBuilder.Entity<HistoryEntry>(entry =>
		{
			entry.HasOne<Player>().WithMany().HasForeignKey(h => h.P1PlayerId).OnDelete(DeleteBehavior.Restrict);
			entry.HasOne<Player>().WithMany().HasForeignKey(h => h.P2PlayerId).OnDelete(DeleteBehavior.Restrict);
			// History is a permanent record — it outlives the tournament/match it came from, so a
			// deletion just clears the (now-meaningless) reference instead of being blocked or cascading.
			entry.HasOne<Tournament>().WithMany().HasForeignKey(h => h.TournamentId).OnDelete(DeleteBehavior.SetNull);
			entry.HasOne<TournamentMatch>().WithMany().HasForeignKey(h => h.TournamentMatchId).OnDelete(DeleteBehavior.SetNull);
			entry.HasIndex(h => h.P1PlayerId);
			entry.HasIndex(h => h.P2PlayerId);
			entry.HasIndex(h => h.TournamentId);
			entry.HasIndex(h => h.LegacyHistoryKey).IsUnique().HasFilter("\"LegacyHistoryKey\" IS NOT NULL");
		});

		modelBuilder.Entity<Archive>(archive =>
		{
			archive.Property(a => a.SeasonName).IsRequired();
			archive.HasIndex(a => a.SeasonName).IsUnique();
			archive.HasMany(a => a.Players).WithOne().HasForeignKey(p => p.ArchiveId).OnDelete(DeleteBehavior.Cascade);
		});

		modelBuilder.Entity<ArchivedPlayerSnapshot>(snapshot =>
		{
			snapshot.Property(s => s.Name).IsRequired();
		});
	}
}
