using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenlessLeague.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTeamMatchSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "WinnerTeamId",
                table: "Tournaments",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "HistoryEntryId2",
                table: "TournamentMatches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "P1TeamId",
                table: "TournamentMatches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "P2TeamId",
                table: "TournamentMatches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TechLossTeamId",
                table: "TournamentMatches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WinnerTeamId",
                table: "TournamentMatches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "PlayerId",
                table: "TournamentChallongeParticipants",
                type: "character varying(128)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)");

            migrationBuilder.AddColumn<Guid>(
                name: "TeamId",
                table: "TournamentChallongeParticipants",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_HistoryEntryId2",
                table: "TournamentMatches",
                column: "HistoryEntryId2");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_P1TeamId",
                table: "TournamentMatches",
                column: "P1TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_P2TeamId",
                table: "TournamentMatches",
                column: "P2TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_TechLossTeamId",
                table: "TournamentMatches",
                column: "TechLossTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_WinnerTeamId",
                table: "TournamentMatches",
                column: "WinnerTeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentChallongeParticipants_TeamId",
                table: "TournamentChallongeParticipants",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentChallongeParticipants_TournamentId_TeamId",
                table: "TournamentChallongeParticipants",
                columns: new[] { "TournamentId", "TeamId" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentChallongeParticipants_Teams_TeamId",
                table: "TournamentChallongeParticipants",
                column: "TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMatches_HistoryEntries_HistoryEntryId2",
                table: "TournamentMatches",
                column: "HistoryEntryId2",
                principalTable: "HistoryEntries",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMatches_Teams_P1TeamId",
                table: "TournamentMatches",
                column: "P1TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMatches_Teams_P2TeamId",
                table: "TournamentMatches",
                column: "P2TeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMatches_Teams_TechLossTeamId",
                table: "TournamentMatches",
                column: "TechLossTeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentMatches_Teams_WinnerTeamId",
                table: "TournamentMatches",
                column: "WinnerTeamId",
                principalTable: "Teams",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TournamentChallongeParticipants_Teams_TeamId",
                table: "TournamentChallongeParticipants");

            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMatches_HistoryEntries_HistoryEntryId2",
                table: "TournamentMatches");

            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMatches_Teams_P1TeamId",
                table: "TournamentMatches");

            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMatches_Teams_P2TeamId",
                table: "TournamentMatches");

            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMatches_Teams_TechLossTeamId",
                table: "TournamentMatches");

            migrationBuilder.DropForeignKey(
                name: "FK_TournamentMatches_Teams_WinnerTeamId",
                table: "TournamentMatches");

            migrationBuilder.DropIndex(
                name: "IX_TournamentMatches_HistoryEntryId2",
                table: "TournamentMatches");

            migrationBuilder.DropIndex(
                name: "IX_TournamentMatches_P1TeamId",
                table: "TournamentMatches");

            migrationBuilder.DropIndex(
                name: "IX_TournamentMatches_P2TeamId",
                table: "TournamentMatches");

            migrationBuilder.DropIndex(
                name: "IX_TournamentMatches_TechLossTeamId",
                table: "TournamentMatches");

            migrationBuilder.DropIndex(
                name: "IX_TournamentMatches_WinnerTeamId",
                table: "TournamentMatches");

            migrationBuilder.DropIndex(
                name: "IX_TournamentChallongeParticipants_TeamId",
                table: "TournamentChallongeParticipants");

            migrationBuilder.DropIndex(
                name: "IX_TournamentChallongeParticipants_TournamentId_TeamId",
                table: "TournamentChallongeParticipants");

            migrationBuilder.DropColumn(
                name: "WinnerTeamId",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "HistoryEntryId2",
                table: "TournamentMatches");

            migrationBuilder.DropColumn(
                name: "P1TeamId",
                table: "TournamentMatches");

            migrationBuilder.DropColumn(
                name: "P2TeamId",
                table: "TournamentMatches");

            migrationBuilder.DropColumn(
                name: "TechLossTeamId",
                table: "TournamentMatches");

            migrationBuilder.DropColumn(
                name: "WinnerTeamId",
                table: "TournamentMatches");

            migrationBuilder.DropColumn(
                name: "TeamId",
                table: "TournamentChallongeParticipants");

            migrationBuilder.AlterColumn<string>(
                name: "PlayerId",
                table: "TournamentChallongeParticipants",
                type: "character varying(128)",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldNullable: true);
        }
    }
}
