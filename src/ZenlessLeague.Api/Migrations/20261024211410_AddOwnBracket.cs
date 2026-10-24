using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenlessLeague.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddOwnBracket : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TournamentMatches_TournamentId_ChallongeMatchId",
                table: "TournamentMatches");

            migrationBuilder.AlterColumn<long>(
                name: "ChallongeMatchId",
                table: "TournamentMatches",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.AddColumn<Guid>(
                name: "LoserToMatchId",
                table: "TournamentMatches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "LoserToSlot",
                table: "TournamentMatches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Number",
                table: "TournamentMatches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Position",
                table: "TournamentMatches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Round",
                table: "TournamentMatches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Section",
                table: "TournamentMatches",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "WinnerToMatchId",
                table: "TournamentMatches",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WinnerToSlot",
                table: "TournamentMatches",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "TournamentParticipants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    Slot = table.Column<int>(type: "integer", nullable: true),
                    FinalRank = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentParticipants", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TournamentParticipants_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentParticipants_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentParticipants_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            // Hand-written: carry over the participants of tournaments run on Challonge (finished
            // ones still need them, e.g. for played counts) before dropping the old table. Their
            // Challonge participant ids aren't needed anymore; Slot/FinalRank stay null.
            migrationBuilder.Sql("""
                INSERT INTO "TournamentParticipants" ("Id", "TournamentId", "PlayerId", "TeamId")
                SELECT gen_random_uuid(), "TournamentId", "PlayerId", "TeamId"
                FROM "TournamentChallongeParticipants";
                """);

            migrationBuilder.DropTable(
                name: "TournamentChallongeParticipants");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_TournamentId_ChallongeMatchId",
                table: "TournamentMatches",
                columns: new[] { "TournamentId", "ChallongeMatchId" },
                unique: true,
                filter: "\"ChallongeMatchId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_TournamentId_Number",
                table: "TournamentMatches",
                columns: new[] { "TournamentId", "Number" },
                unique: true,
                filter: "\"Number\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentParticipants_PlayerId",
                table: "TournamentParticipants",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentParticipants_TeamId",
                table: "TournamentParticipants",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentParticipants_TournamentId_PlayerId",
                table: "TournamentParticipants",
                columns: new[] { "TournamentId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentParticipants_TournamentId_TeamId",
                table: "TournamentParticipants",
                columns: new[] { "TournamentId", "TeamId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TournamentParticipants");

            migrationBuilder.DropIndex(
                name: "IX_TournamentMatches_TournamentId_ChallongeMatchId",
                table: "TournamentMatches");

            migrationBuilder.DropIndex(
                name: "IX_TournamentMatches_TournamentId_Number",
                table: "TournamentMatches");

            migrationBuilder.DropColumn(
                name: "LoserToMatchId",
                table: "TournamentMatches");

            migrationBuilder.DropColumn(
                name: "LoserToSlot",
                table: "TournamentMatches");

            migrationBuilder.DropColumn(
                name: "Number",
                table: "TournamentMatches");

            migrationBuilder.DropColumn(
                name: "Position",
                table: "TournamentMatches");

            migrationBuilder.DropColumn(
                name: "Round",
                table: "TournamentMatches");

            migrationBuilder.DropColumn(
                name: "Section",
                table: "TournamentMatches");

            migrationBuilder.DropColumn(
                name: "WinnerToMatchId",
                table: "TournamentMatches");

            migrationBuilder.DropColumn(
                name: "WinnerToSlot",
                table: "TournamentMatches");

            migrationBuilder.AlterColumn<long>(
                name: "ChallongeMatchId",
                table: "TournamentMatches",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateTable(
                name: "TournamentChallongeParticipants",
                columns: table => new
                {
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChallongeParticipantId = table.Column<string>(type: "text", nullable: false),
                    PlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentChallongeParticipants", x => new { x.TournamentId, x.ChallongeParticipantId });
                    table.ForeignKey(
                        name: "FK_TournamentChallongeParticipants_Players_PlayerId",
                        column: x => x.PlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentChallongeParticipants_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_TournamentChallongeParticipants_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentMatches_TournamentId_ChallongeMatchId",
                table: "TournamentMatches",
                columns: new[] { "TournamentId", "ChallongeMatchId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentChallongeParticipants_PlayerId",
                table: "TournamentChallongeParticipants",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentChallongeParticipants_TeamId",
                table: "TournamentChallongeParticipants",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentChallongeParticipants_TournamentId_PlayerId",
                table: "TournamentChallongeParticipants",
                columns: new[] { "TournamentId", "PlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TournamentChallongeParticipants_TournamentId_TeamId",
                table: "TournamentChallongeParticipants",
                columns: new[] { "TournamentId", "TeamId" },
                unique: true);
        }
    }
}
