using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenlessLeague.Api.Migrations
{
    /// <inheritdoc />
    public partial class TagTournamentTechLossHistoryEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Bracket-match tech losses used to be recorded with Kind = None (0), so history showed
            // the stale typed result instead of "tech loss". Kind 2 = TournamentMatchKind.TechLoss.
            migrationBuilder.Sql("""
                UPDATE "HistoryEntries" h
                SET "Kind" = 2
                FROM "TournamentMatches" m
                WHERE h."Kind" = 0
                  AND (m."TechLossPlayerId" IS NOT NULL OR m."TechLossTeamId" IS NOT NULL)
                  AND h."Id" IN (m."HistoryEntryId", m."HistoryEntryId2");
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE "HistoryEntries" h
                SET "Kind" = 0
                FROM "TournamentMatches" m
                WHERE h."Kind" = 2
                  AND h."TournamentMatchId" IS NOT NULL
                  AND h."Id" IN (m."HistoryEntryId", m."HistoryEntryId2");
                """);
        }
    }
}
