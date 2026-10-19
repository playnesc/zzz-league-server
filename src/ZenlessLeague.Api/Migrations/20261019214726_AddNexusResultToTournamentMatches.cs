using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenlessLeague.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddNexusResultToTournamentMatches : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "NexusP1IsLeft",
                table: "TournamentMatches",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NexusResultId",
                table: "TournamentMatches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "NexusSnapshot",
                table: "TournamentMatches",
                type: "jsonb",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NexusP1IsLeft",
                table: "TournamentMatches");

            migrationBuilder.DropColumn(
                name: "NexusResultId",
                table: "TournamentMatches");

            migrationBuilder.DropColumn(
                name: "NexusSnapshot",
                table: "TournamentMatches");
        }
    }
}
