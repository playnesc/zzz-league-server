using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenlessLeague.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddRegistrationScreenshotUrls : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Player1_HoyolabScreenshotUrl",
                table: "TournamentRegistrations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Player1_RosterScreenshotUrl",
                table: "TournamentRegistrations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Player2_HoyolabScreenshotUrl",
                table: "TournamentRegistrations",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Player2_RosterScreenshotUrl",
                table: "TournamentRegistrations",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Player1_HoyolabScreenshotUrl",
                table: "TournamentRegistrations");

            migrationBuilder.DropColumn(
                name: "Player1_RosterScreenshotUrl",
                table: "TournamentRegistrations");

            migrationBuilder.DropColumn(
                name: "Player2_HoyolabScreenshotUrl",
                table: "TournamentRegistrations");

            migrationBuilder.DropColumn(
                name: "Player2_RosterScreenshotUrl",
                table: "TournamentRegistrations");
        }
    }
}
