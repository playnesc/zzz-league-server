using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenlessLeague.Api.Migrations
{
    /// <inheritdoc />
    // Maps Tournament.Version / TournamentMatch.Version to Postgres's built-in xmin system column.
    // Npgsql's SQL generator skips xmin, so the AddColumn/DropColumn below emit no SQL — this
    // migration only brings the model snapshot up to date (verified with `dotnet ef migrations script`).
    public partial class AddConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "Tournaments",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);

            migrationBuilder.AddColumn<uint>(
                name: "xmin",
                table: "TournamentMatches",
                type: "xid",
                rowVersion: true,
                nullable: false,
                defaultValue: 0u);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "xmin",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "xmin",
                table: "TournamentMatches");
        }
    }
}
