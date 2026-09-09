using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenlessLeague.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddLegacyTraceabilityColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LegacyId",
                table: "Tournaments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LegacyHistoryKey",
                table: "HistoryEntries",
                type: "text",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tournaments_LegacyId",
                table: "Tournaments",
                column: "LegacyId",
                unique: true,
                filter: "\"LegacyId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_HistoryEntries_LegacyHistoryKey",
                table: "HistoryEntries",
                column: "LegacyHistoryKey",
                unique: true,
                filter: "\"LegacyHistoryKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Tournaments_LegacyId",
                table: "Tournaments");

            migrationBuilder.DropIndex(
                name: "IX_HistoryEntries_LegacyHistoryKey",
                table: "HistoryEntries");

            migrationBuilder.DropColumn(
                name: "LegacyId",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "LegacyHistoryKey",
                table: "HistoryEntries");
        }
    }
}
