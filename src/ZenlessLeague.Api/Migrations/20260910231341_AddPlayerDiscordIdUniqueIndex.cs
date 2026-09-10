using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenlessLeague.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayerDiscordIdUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Pre-existing stale data from the old RTDB-based Discord linking flow left more than
            // one player sharing the same DiscordId in at least one environment — clear the
            // ambiguous value on every row in such a group so the unique index below can be
            // created; affected players just need to re-link via Settings afterward. No Elo/
            // tournament/history data is touched.
            migrationBuilder.Sql("""
                UPDATE "Players"
                SET "DiscordId" = NULL, "DiscordUsername" = NULL
                WHERE "DiscordId" IN (
                    SELECT "DiscordId" FROM "Players"
                    WHERE "DiscordId" IS NOT NULL
                    GROUP BY "DiscordId"
                    HAVING COUNT(*) > 1
                );
                """);

            migrationBuilder.CreateIndex(
                name: "IX_Players_DiscordId",
                table: "Players",
                column: "DiscordId",
                unique: true,
                filter: "\"DiscordId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Players_DiscordId",
                table: "Players");
        }
    }
}
