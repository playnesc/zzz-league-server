using ZenlessLeague.Api.Features.Discord;
using ZenlessLeague.Api.Features.History;
using ZenlessLeague.Api.Features.Players;
using ZenlessLeague.Api.Features.Teams;
using ZenlessLeague.Api.Features.Tournaments.Challonge;
using ZenlessLeague.Api.Features.Tournaments.Matches;
using ZenlessLeague.Api.Features.Tournaments.Realtime;

namespace ZenlessLeague.Api.Features.Tournaments;

public interface ITournamentService
{
    Task<TournamentDto> CreateAsync(CreateTournamentRequest request, CancellationToken ct = default);
    Task<TournamentDto?> GetAsync(Guid id, CancellationToken ct = default);
    Task<List<TournamentDto>> ListAsync(CancellationToken ct = default);

    // Port of functions/admin/tournament/closeTournamentRegistration.js.
    Task<TournamentDto?> CloseRegistrationAsync(Guid id, CancellationToken ct = default);

    // Port of functions/admin/tournament/createChallongeBracket.js. Currently solo-only —
    // 2v2 bracket support was never decided for the legacy app either.
    Task<TournamentDto?> CreateChallongeBracketAsync(Guid id, CancellationToken ct = default);

    // Port of functions/admin/tournament/startChallongeTournament.js.
    Task<TournamentDto?> StartAsync(Guid id, CancellationToken ct = default);

    // Port of functions/admin/tournament/finishTournament.js.
    Task<TournamentDto?> FinishAsync(Guid id, CancellationToken ct = default);

    // Port of functions/admin/tournament/deleteTournament.js. Deletion always happens even if
    // Challonge/Discord cleanup fails — a TournamentLifecycleException after a successful delete
    // means "deleted, but cleanup needs manual attention", not "not deleted".
    Task<bool> DeleteAsync(Guid id, CancellationToken ct = default);

    // Port of functions/admin/tournament/splitTournament.js.
    Task<SplitTournamentResult?> SplitAsync(Guid id, SplitTournamentRequest request, CancellationToken ct = default);

    // Port of functions/admin/tournament/refreshTournamentGames.js.
    Task RefreshGamesAsync(Guid id, CancellationToken ct = default);

    // Ports of functions/admin/tournament/{incrementTournamentCount,incrementSeasonalTournamentCount}.js.
    Task IncrementTournamentCountAsync(Guid id, bool seasonal, CancellationToken ct = default);
}

/// <summary>Thrown for tournament lifecycle validation failures (wrong state, missing bracket, etc.) — maps to 400.</summary>
public class TournamentLifecycleException(string message) : Exception(message);

public class TournamentService(
    ITournamentRepository tournamentRepository,
    ITournamentRegistrationRepository registrationRepository,
    ITeamRepository teamRepository,
    IPlayerRepository playerRepository,
    ITournamentChallongeParticipantRepository challongeParticipantRepository,
    IChallongeClient challongeClient,
    IMatchService matchService,
    IHistoryService historyService,
    ITournamentDiscordResourcesService discordResourcesService,
    ITournamentNotifier notifier) : ITournamentService
{
    public async Task<TournamentDto> CreateAsync(CreateTournamentRequest request, CancellationToken ct = default)
    {
        var tournament = await tournamentRepository.CreateAsync(new Tournament
        {
            Id = Guid.NewGuid(),
            Name = request.Name,
            Description = request.Description,
            RegistrationStartDate = request.RegistrationStartDate,
            RegistrationEndDate = request.RegistrationEndDate,
            TournamentStartDate = request.TournamentStartDate,
            TournamentEndDate = request.TournamentEndDate,
            MinCost = request.MinCost,
            MaxCost = request.MaxCost,
            MinCharacters = request.MinCharacters,
            MinTier = request.MinTier,
            MaxTier = request.MaxTier,
            RegistrationType = request.RegistrationType,
            BracketType = request.BracketType,
            Visible = request.Visible,
        }, ct);

        await notifier.TournamentChangedAsync(tournament.Id, ct);
        return ToDto(tournament);
    }

    public async Task<TournamentDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var tournament = await tournamentRepository.GetByIdAsync(id, ct);
        return tournament is null ? null : ToDto(tournament);
    }

    public async Task<List<TournamentDto>> ListAsync(CancellationToken ct = default) =>
        (await tournamentRepository.ListAsync(ct)).Select(ToDto).ToList();

    public async Task<TournamentDto?> CloseRegistrationAsync(Guid id, CancellationToken ct = default)
    {
        var tournament = await tournamentRepository.GetByIdAsync(id, ct);
        if (tournament is null)
        {
            return null;
        }

        if (!TournamentStateMachine.IsRegistrationOpen(tournament.State))
        {
            throw new TournamentLifecycleException("Tournament is not currently accepting registrations.");
        }

        tournament.State = TournamentState.RegistrationClosed;
        await tournamentRepository.SaveChangesAsync(ct);
        await notifier.TournamentChangedAsync(id, ct);
        return ToDto(tournament);
    }

    public async Task<TournamentDto?> CreateChallongeBracketAsync(Guid id, CancellationToken ct = default)
    {
        var tournament = await tournamentRepository.GetByIdAsync(id, ct);
        if (tournament is null)
        {
            return null;
        }

        if (TournamentStateMachine.IsLocked(tournament.State))
        {
            throw new TournamentLifecycleException("Challonge bracket has already been created.");
        }

        if (tournament.RegistrationType != TournamentRegistrationType.Solo)
        {
            throw new TournamentLifecycleException("Challonge bracket creation currently only supports solo tournaments.");
        }

        var approved = (await registrationRepository.ListByTournamentAsync(id, ct)).Where(r => r.Approved).ToList();
        if (approved.Count < 2)
        {
            throw new TournamentLifecycleException("Need at least 2 approved players.");
        }

        var tournamentType = tournament.BracketType ?? "single elimination";

        var participants = new List<(string Name, string Uid)>();
        foreach (var registration in approved)
        {
            var player = await playerRepository.GetByIdAsync(registration.PlayerId!, ct)
                ?? throw new TournamentLifecycleException($"Player '{registration.PlayerId}' not found.");
            participants.Add((player.Name, player.Uid));
        }

        var created = await challongeClient.CreateTournamentAsync(
            tournament.Name, tournamentType, tournament.Description, tournament.TournamentStartDate,
            tournament.ConsolationMatchesTargetRank, ct);

        await challongeClient.BulkAddParticipantsAsync(created.ChallongeTournamentId, participants, ct);
        var randomized = await challongeClient.RandomizeParticipantsAsync(created.ChallongeTournamentId, participants, ct);

        await challongeParticipantRepository.AddRangeAsync(
            randomized.Where(r => r.Misc is not null).Select(r => new TournamentChallongeParticipant
            {
                TournamentId = id,
                ChallongeParticipantId = r.ChallongeParticipantId,
                PlayerId = r.Misc!,
            }), ct);

        tournament.State = TournamentState.BracketCreated;
        tournament.ChallongeTournamentId = created.ChallongeTournamentId;
        tournament.ChallongeTournamentUrl = created.ChallongeTournamentUrl;
        await tournamentRepository.SaveChangesAsync(ct);
        await notifier.TournamentChangedAsync(id, ct);

        return ToDto(tournament);
    }

    public async Task<TournamentDto?> StartAsync(Guid id, CancellationToken ct = default)
    {
        var tournament = await tournamentRepository.GetByIdAsync(id, ct);
        if (tournament is null)
        {
            return null;
        }

        if (TournamentStateMachine.HasStarted(tournament.State))
        {
            throw new TournamentLifecycleException("Tournament has already started.");
        }

        if (!TournamentStateMachine.IsBracketCreated(tournament.State) || tournament.ChallongeTournamentId is null)
        {
            throw new TournamentLifecycleException("Challonge bracket has not been created yet.");
        }

        await challongeClient.StartAsync(tournament.ChallongeTournamentId, ct);

        tournament.State = TournamentState.Started;
        await tournamentRepository.SaveChangesAsync(ct);
        await notifier.TournamentChangedAsync(id, ct);

        var now = DateTimeOffset.UtcNow;
        var participants = await challongeParticipantRepository.ListByTournamentAsync(id, ct);
        foreach (var uid in participants.Select(p => p.PlayerId).Distinct())
        {
            var player = await playerRepository.GetEntityByIdAsync(uid, ct);
            if (player is not null)
            {
                player.LastPlayedTournamentTimestamp = now;
            }
        }

        await playerRepository.SaveChangesAsync(ct);

        // State transition + timestamps are already committed above — these follow-up steps are
        // best-effort and accumulated, matching legacy startChallongeTournament.js: a failure here
        // surfaces as an error to the admin without leaving the tournament in a half-started state.
        var followUpErrors = new List<string>();

        try
        {
            await matchService.SyncFromChallongeAsync(id, ct);
        }
        catch (Exception ex)
        {
            followUpErrors.Add($"Match sync: {ex.Message}");
        }

        try
        {
            await discordResourcesService.CreateResourcesAsync(id, ct);
        }
        catch (Exception ex)
        {
            followUpErrors.Add($"Discord: {ex.Message}");
        }

        if (followUpErrors.Count > 0)
        {
            throw new TournamentLifecycleException($"Tournament started, but follow-up steps failed: {string.Join("; ", followUpErrors)}");
        }

        return ToDto(tournament);
    }

    public async Task<TournamentDto?> FinishAsync(Guid id, CancellationToken ct = default)
    {
        var tournament = await tournamentRepository.GetByIdAsync(id, ct);
        if (tournament is null)
        {
            return null;
        }

        if (tournament.ChallongeTournamentId is null)
        {
            throw new TournamentLifecycleException("Tournament has no Challonge bracket.");
        }

        await challongeClient.FinalizeAsync(tournament.ChallongeTournamentId, ct);

        var participants = await challongeClient.GetAllParticipantsAsync(tournament.ChallongeTournamentId, ct);
        var winner = participants.FirstOrDefault(p => p.FinalRank == 1)
            ?? throw new TournamentLifecycleException("Could not find a participant with final_rank 1.");

        var challongeToUid = (await challongeParticipantRepository.ListByTournamentAsync(id, ct))
            .ToDictionary(p => p.ChallongeParticipantId, p => p.PlayerId);

        if (!challongeToUid.TryGetValue(winner.ChallongeParticipantId, out var winnerUid))
        {
            throw new TournamentLifecycleException("Tournament winner is not a known participant.");
        }

        tournament.State = TournamentState.Complete;
        tournament.ChallongeWinnerId = winner.ChallongeParticipantId;
        tournament.WinnerId = winnerUid;
        await tournamentRepository.SaveChangesAsync(ct);
        await notifier.TournamentChangedAsync(id, ct);

        var isSeasonal = (tournament.OverrideEloChange ?? -1) == -1;

        foreach (var participant in participants)
        {
            if (!challongeToUid.TryGetValue(participant.ChallongeParticipantId, out var uid))
            {
                continue;
            }

            var player = await playerRepository.GetEntityByIdAsync(uid, ct);
            if (player is null)
            {
                continue;
            }

            player.PlayedTournamentCount++;
            if (isSeasonal)
            {
                player.SeasonalPlayedTournamentCount++;
            }
        }

        await playerRepository.SaveChangesAsync(ct);

        var prizeEloBonus = isSeasonal
            ? new Dictionary<int, int> { [1] = 40, [2] = 20, [3] = 10 }
            : new Dictionary<int, int> { [1] = 15, [2] = 10 };

        foreach (var participant in participants)
        {
            if (participant.FinalRank is not int rank || !prizeEloBonus.TryGetValue(rank, out var bonus))
            {
                continue;
            }

            if (!challongeToUid.TryGetValue(participant.ChallongeParticipantId, out var uid))
            {
                continue;
            }

            var player = await playerRepository.GetEntityByIdAsync(uid, ct);
            if (player is null)
            {
                continue;
            }

            await historyService.SetPlayerEloAsync(uid, player.Elo + bonus, ct);
        }

        // Unguarded, matching legacy finishTournament.js — the completion state above is already
        // committed, so a Discord failure here surfaces as an error without undoing the finish.
        await discordResourcesService.DeleteChannelAsync(id, ct);
        await discordResourcesService.DeleteRoleAsync(id, ct);

        return ToDto(tournament);
    }

    public async Task<bool> DeleteAsync(Guid id, CancellationToken ct = default)
    {
        var tournament = await tournamentRepository.GetByIdAsync(id, ct);
        if (tournament is null)
        {
            return false;
        }

        var cleanupErrors = new List<string>();

        if (tournament.ChallongeTournamentId is not null)
        {
            try
            {
                await challongeClient.DeleteTournamentAsync(tournament.ChallongeTournamentId, ct);
            }
            catch (ChallongeApiException ex)
            {
                cleanupErrors.Add($"Challonge: {ex.Message}");
            }
        }

        try
        {
            await discordResourcesService.DeleteChannelAsync(id, ct);
        }
        catch (Exception ex)
        {
            cleanupErrors.Add($"Discord channel: {ex.Message}");
        }

        try
        {
            await discordResourcesService.DeleteRoleAsync(id, ct);
        }
        catch (Exception ex)
        {
            cleanupErrors.Add($"Discord role: {ex.Message}");
        }

        // Deletion happens regardless of cleanup failures, matching legacy deleteTournament.js —
        // best-effort external cleanup shouldn't leave an orphaned tournament row behind.
        await tournamentRepository.DeleteAsync(id, ct);
        await notifier.TournamentChangedAsync(id, ct);

        if (cleanupErrors.Count > 0)
        {
            throw new TournamentLifecycleException($"Tournament deleted, but cleanup failed: {string.Join("; ", cleanupErrors)}");
        }

        return true;
    }

    public async Task<SplitTournamentResult?> SplitAsync(Guid id, SplitTournamentRequest request, CancellationToken ct = default)
    {
        if (request.DivisionGroups.Count < 2 || request.DivisionGroups.Any(g => g.Count < 2))
        {
            throw new TournamentLifecycleException(
                "divisionGroups must be an array of >=2 groups, each with >=2 players.");
        }

        var tournament = await tournamentRepository.GetByIdAsync(id, ct);
        if (tournament is null)
        {
            return null;
        }

        if (TournamentStateMachine.IsLocked(tournament.State) || tournament.ChallongeTournamentId is not null)
        {
            throw new TournamentLifecycleException("Tournament has already started.");
        }

        if (tournament.DivisionGroupId is not null)
        {
            throw new TournamentLifecycleException("Tournament has already been split into divisions.");
        }

        if (tournament.RegistrationType != TournamentRegistrationType.Solo)
        {
            throw new TournamentLifecycleException("Splitting into divisions currently only supports solo tournaments.");
        }

        var approved = (await registrationRepository.ListByTournamentAsync(id, ct)).Where(r => r.Approved).ToList();
        var approvedUids = approved.Select(r => r.PlayerId!).ToHashSet();

        var groupedUids = request.DivisionGroups.SelectMany(g => g).ToList();
        var groupedUidsSet = groupedUids.ToHashSet();
        if (groupedUids.Count != groupedUidsSet.Count ||
            groupedUids.Count != approved.Count ||
            !groupedUids.All(approvedUids.Contains))
        {
            throw new TournamentLifecycleException("divisionGroups must contain every approved registration exactly once.");
        }

        var registrationsByUid = approved.ToDictionary(r => r.PlayerId!, r => r);
        var groups = request.DivisionGroups.Select(g => g.Select(uid => registrationsByUid[uid]).ToList()).ToList();

        var closedRegistrationEndDate = tournament.RegistrationEndDate < DateTimeOffset.UtcNow
            ? tournament.RegistrationEndDate
            : DateTimeOffset.UtcNow;

        var originalName = tournament.Name;
        var originalDiscordChannelName = tournament.DiscordChannelName;
        var originalId = tournament.Id;

        var division1Uids = groups[0].Select(r => r.PlayerId!).ToHashSet();

        tournament.Name = $"{originalName} A";
        if (originalDiscordChannelName is not null)
        {
            tournament.DiscordChannelName = $"{originalDiscordChannelName}-A";
        }

        tournament.DivisionGroupId = originalId.ToString();
        tournament.DivisionIndex = 1;
        tournament.RegistrationEndDate = closedRegistrationEndDate;
        tournament.State = TournamentState.RegistrationClosed;

        foreach (var registration in approved.Where(r => !division1Uids.Contains(r.PlayerId!)))
        {
            await registrationRepository.DeleteAsync(registration, ct);
        }

        await tournamentRepository.SaveChangesAsync(ct);

        var divisionTournamentIds = new List<Guid> { originalId };

        for (var i = 1; i < groups.Count; i++)
        {
            var divisionIndex = i + 1;
            var divisionLetter = (char)('A' + i);

            var newTournament = new Tournament
            {
                Id = Guid.NewGuid(),
                Name = $"{originalName} {divisionLetter}",
                Description = tournament.Description,
                RegistrationStartDate = tournament.RegistrationStartDate,
                RegistrationEndDate = closedRegistrationEndDate,
                TournamentStartDate = tournament.TournamentStartDate,
                TournamentEndDate = tournament.TournamentEndDate,
                MinCost = tournament.MinCost,
                MaxCost = tournament.MaxCost,
                MinCharacters = tournament.MinCharacters,
                MinTier = tournament.MinTier,
                MaxTier = tournament.MaxTier,
                RegistrationType = tournament.RegistrationType,
                BracketType = tournament.BracketType,
                OverrideEloChange = tournament.OverrideEloChange,
                ConsolationMatchesTargetRank = tournament.ConsolationMatchesTargetRank,
                Visible = tournament.Visible,
                DiscordRoleName = tournament.DiscordRoleName,
                DiscordChannelName = originalDiscordChannelName is not null ? $"{originalDiscordChannelName}-{divisionLetter}" : null,
                State = TournamentState.RegistrationClosed,
                DivisionGroupId = originalId.ToString(),
                DivisionIndex = divisionIndex,
            };

            await tournamentRepository.CreateAsync(newTournament, ct);
            divisionTournamentIds.Add(newTournament.Id);

            foreach (var registration in groups[i])
            {
                await registrationRepository.CreateAsync(new TournamentRegistration
                {
                    Id = Guid.NewGuid(),
                    TournamentId = newTournament.Id,
                    Type = registration.Type,
                    PlayerId = registration.PlayerId,
                    TeamId = registration.TeamId,
                    Player1Details = CloneDetails(registration.Player1Details),
                    Player2Details = registration.Player2Details is null ? null : CloneDetails(registration.Player2Details),
                    Approved = true,
                    RegisteredAt = registration.RegisteredAt,
                    UpdatedAt = DateTimeOffset.UtcNow,
                }, ct);
            }
        }

        foreach (var divisionTournamentId in divisionTournamentIds)
        {
            await notifier.TournamentChangedAsync(divisionTournamentId, ct);
        }

        return new SplitTournamentResult(divisionTournamentIds);
    }

    public async Task RefreshGamesAsync(Guid id, CancellationToken ct = default) =>
        await matchService.SyncFromChallongeAsync(id, ct);

    public async Task IncrementTournamentCountAsync(Guid id, bool seasonal, CancellationToken ct = default)
    {
        var approved = (await registrationRepository.ListByTournamentAsync(id, ct)).Where(r => r.Approved).ToList();
        if (approved.Count == 0)
        {
            throw new TournamentLifecycleException("No approved participants found for this tournament.");
        }

        var uids = new List<string>();
        foreach (var registration in approved)
        {
            if (registration.PlayerId is not null)
            {
                uids.Add(registration.PlayerId);
            }
            else if (registration.TeamId is Guid teamId)
            {
                var team = await teamRepository.GetByIdAsync(teamId, ct);
                if (team is not null)
                {
                    uids.Add(team.CreatorPlayerId);
                    uids.Add(team.Player2PlayerId);
                }
            }
        }

        foreach (var uid in uids.Distinct())
        {
            var player = await playerRepository.GetEntityByIdAsync(uid, ct);
            if (player is null)
            {
                continue;
            }

            player.PlayedTournamentCount++;
            if (seasonal)
            {
                player.SeasonalPlayedTournamentCount++;
            }
        }

        await playerRepository.SaveChangesAsync(ct);
    }

    private static PlayerRegistrationDetails CloneDetails(PlayerRegistrationDetails d) => new()
    {
        GameUid = d.GameUid,
        PrizeAsMoney = d.PrizeAsMoney,
        PrizeUid = d.PrizeUid,
        DarteNickname = d.DarteNickname,
        DartePresetName = d.DartePresetName,
        RosterName = d.RosterName,
    };

    private static TournamentDto ToDto(Tournament t) => new(
        t.Id, t.Name, t.Description,
        t.RegistrationStartDate, t.RegistrationEndDate, t.TournamentStartDate, t.TournamentEndDate,
        t.MinCost, t.MaxCost, t.MinCharacters, t.MinTier, t.MaxTier,
        t.State, t.RegistrationType, t.Visible, t.BracketType,
        t.ChallongeTournamentId, t.ChallongeTournamentUrl, t.ChallongeWinnerId, t.WinnerId,
        t.DivisionGroupId, t.DivisionIndex);
}
