using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenlessLeague.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamForeignKeyOnRegistration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TournamentRegistrations_TeamId",
                table: "TournamentRegistrations",
                column: "TeamId");

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentRegistrations_Teams_TeamId",
                table: "TournamentRegistrations",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TournamentRegistrations_Teams_TeamId",
                table: "TournamentRegistrations");

            migrationBuilder.DropIndex(
                name: "IX_TournamentRegistrations_TeamId",
                table: "TournamentRegistrations");
        }
    }
}
