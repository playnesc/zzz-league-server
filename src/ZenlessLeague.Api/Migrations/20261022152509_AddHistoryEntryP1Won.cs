using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenlessLeague.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoryEntryP1Won : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "P1Won",
                table: "HistoryEntries",
                type: "boolean",
                nullable: true);

            // Backfill existing two-player entries, most authoritative source first. Single-player
            // adjustments (P2Change NULL) stay NULL, as do ties with no linked match — reversal then
            // falls back to comparing P1Change/P2Change, same as before this column existed.

            // 1. Solo bracket matches: the linked match's winner.
            migrationBuilder.Sql("""
                UPDATE "HistoryEntries" h
                SET "P1Won" = (m."WinnerPlayerId" = h."P1PlayerId")
                FROM "TournamentMatches" m
                WHERE h."P2Change" IS NOT NULL
                  AND m."WinnerPlayerId" IS NOT NULL
                  AND h."Id" = m."HistoryEntryId";
                """);

            // 2. Team bracket matches (two entries per match): P1 won if they're on the winning team.
            migrationBuilder.Sql("""
                UPDATE "HistoryEntries" h
                SET "P1Won" = EXISTS (
                    SELECT 1 FROM "Teams" t
                    WHERE t."Id" = m."WinnerTeamId"
                      AND h."P1PlayerId" IN (t."Player1Id", t."Player2Id"))
                FROM "TournamentMatches" m
                WHERE h."P2Change" IS NOT NULL
                  AND h."P1Won" IS NULL
                  AND m."WinnerTeamId" IS NOT NULL
                  AND h."Id" IN (m."HistoryEntryId", m."HistoryEntryId2");
                """);

            // 3. Everything else (ad-hoc/custom matches, unlinked legacy entries): the bigger change won.
            migrationBuilder.Sql("""
                UPDATE "HistoryEntries"
                SET "P1Won" = ("P1Change" > "P2Change")
                WHERE "P2Change" IS NOT NULL
                  AND "P1Won" IS NULL
                  AND "P1Change" <> "P2Change";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "P1Won",
                table: "HistoryEntries");
        }
    }
}
