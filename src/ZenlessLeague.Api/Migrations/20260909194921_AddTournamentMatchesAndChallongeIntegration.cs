using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenlessLeague.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentMatchesAndChallongeIntegration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HistoryEntries_Tournaments_TournamentId",
                table: "HistoryEntries");

            migrationBuilder.AddColumn<string>(
                name: "ChallongeWinnerId",
                table: "Tournaments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WinnerId",
                table: "Tournaments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "TournamentMatchId",
                table: "HistoryEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TournamentChallongeParticipants",
                columns: table => new
                {
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallongeParticipantId = table.Column<string>(type: "text", nullable: false),
                    PlayerId = table.Column<string>(type: "character varying(128)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentChallongeParticipants", x => new { x.TournamentId, x.ChallongeParticipantId });
                    table.ForeignKey(
                        name: "FK_TournamentChallongeParticipants_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Uid",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentChallongeParticipants_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TournamentMatches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallongeMatchId = table.Column<long>(type: "bigint", nullable: false),
                    P1PlayerId = table.Column<string>(type: "character varying(128)", nullable: true),
                    P2PlayerId = table.Column<string>(type: "character varying(128)", nullable: true),
                    State = table.Column<string>(type: "text", nullable: false),
                    WinnerPlayerId = table.Column<string>(type: "character varying(128)", nullable: true),
                    ResultScreenshotUrl = table.Column<string>(type: "text", nullable: true),
                    ResultP1 = table.Column<string>(type: "text", nullable: true),
                    ResultP2 = table.Column<string>(type: "text", nullable: true),
                    P1ApprovedResult = table.Column<bool>(type: "boolean", nullable: false),
                    P2ApprovedResult = table.Column<bool>(type: "boolean", nullable: false),
                    TechLossPlayerId = table.Column<string>(type: "character varying(128)", nullable: true),
                    HistoryEntryId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentMatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentMatches_HistoryEntries_HistoryEntryId",
                        column: x => x.HistoryEntryId,
                        principalTable: "HistoryEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_TournamentMatches_Players_P1PlayerId",
                        column: x => x.P1PlayerId,
                        principalTable: "Players",
                        principalColumn: "Uid",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentMatches_Players_P2PlayerId",
                        column: x => x.P2PlayerId,
                        principalTable: "Players",
                        principalColumn: "Uid",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentMatches_Players_TechLossPlayerId",
                        column: x => x.TechLossPlayerId,
                        principalTable: "Players",
                        principalColumn: "Uid",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentMatches_Players_WinnerPlayerId",
                        column: x => x.WinnerPlayerId,
                        principalTable: "Players",
                        principalColumn: "Uid",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentMatches_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HistoryEntries_TournamentMatchId",
                table: "HistoryEntries",
                column: "TournamentMatchId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentChallongeParticipants_PlayerId",
                table: "TournamentChallongeParticipants",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentChallongeParticipants_TournamentId_PlayerId",
                table: "TournamentChallongeParticipants",
                columns: new[] { "TournamentId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_HistoryEntryId",
                table: "TournamentMatches",
                column: "HistoryEntryId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_P1PlayerId",
                table: "TournamentMatches",
                column: "P1PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_P2PlayerId",
                table: "TournamentMatches",
                column: "P2PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_TechLossPlayerId",
                table: "TournamentMatches",
                column: "TechLossPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_TournamentId_ChallongeMatchId",
                table: "TournamentMatches",
                columns: new[] { "TournamentId", "ChallongeMatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_WinnerPlayerId",
                table: "TournamentMatches",
                column: "WinnerPlayerId");

            migrationBuilder.AddForeignKey(
                name: "FK_HistoryEntries_TournamentMatches_TournamentMatchId",
                table: "HistoryEntries",
                column: "TournamentMatchId",
                principalTable: "TournamentMatches",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_HistoryEntries_Tournaments_TournamentId",
                table: "HistoryEntries",
                column: "TournamentId",
                principalTable: "Tournaments",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentRegistrations_Tournaments_TournamentId",
                table: "TournamentRegistrations",
                column: "TournamentId",
                principalTable: "Tournaments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_HistoryEntries_TournamentMatches_TournamentMatchId",
                table: "HistoryEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_HistoryEntries_Tournaments_TournamentId",
                table: "HistoryEntries");

            migrationBuilder.DropForeignKey(
                name: "FK_TournamentRegistrations_Tournaments_TournamentId",
                table: "TournamentRegistrations");

            migrationBuilder.DropTable(
                name: "TournamentChallongeParticipants");

            migrationBuilder.DropTable(
                name: "TournamentMatches");

            migrationBuilder.DropIndex(
                name: "IX_HistoryEntries_TournamentMatchId",
                table: "HistoryEntries");

            migrationBuilder.DropColumn(
                name: "ChallongeWinnerId",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "WinnerId",
                table: "Tournaments");

            migrationBuilder.DropColumn(
                name: "TournamentMatchId",
                table: "HistoryEntries");

            migrationBuilder.AddForeignKey(
                name: "FK_HistoryEntries_Tournaments_TournamentId",
                table: "HistoryEntries",
                column: "TournamentId",
                principalTable: "Tournaments",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }
    }
}
