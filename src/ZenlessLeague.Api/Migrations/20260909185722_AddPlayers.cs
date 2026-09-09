using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenlessLeague.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddPlayers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "PlayerId",
                table: "TournamentRegistrations",
                type: "character varying(128)",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Player2PlayerId",
                table: "Teams",
                type: "character varying(128)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "CreatorPlayerId",
                table: "Teams",
                type: "character varying(128)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.CreateTable(
                name: "Players",
                columns: table => new
                {
                    Uid = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DiscordId = table.Column<string>(type: "text", nullable: true),
                    DiscordUsername = table.Column<string>(type: "text", nullable: true),
                    Elo = table.Column<int>(type: "integer", nullable: false),
                    TournamentPoints = table.Column<int>(type: "integer", nullable: false),
                    IsMidConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    IsHighConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    Wins = table.Column<int>(type: "integer", nullable: false),
                    Losses = table.Column<int>(type: "integer", nullable: false),
                    PlayedTournamentCount = table.Column<int>(type: "integer", nullable: false),
                    SeasonalPlayedTournamentCount = table.Column<int>(type: "integer", nullable: false),
                    LastPlayedTournamentTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    LastEloUpdateTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Avatar = table.Column<string>(type: "text", nullable: true),
                    Role = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    LastRegistration_GameUid = table.Column<string>(type: "text", nullable: true),
                    LastRegistration_PrizeAsMoney = table.Column<bool>(type: "boolean", nullable: true),
                    LastRegistration_PrizeUid = table.Column<string>(type: "text", nullable: true),
                    LastRegistration_DarteNickname = table.Column<string>(type: "text", nullable: true),
                    LastRegistration_DartePresetName = table.Column<string>(type: "text", nullable: true),
                    LastRegistration_RosterName = table.Column<string>(type: "text", nullable: true),
                    LastRegistration_RosterScreenshotUrl = table.Column<string>(type: "text", nullable: true),
                    LastRegistration_HoyolabScreenshotUrl = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Players", x => x.Uid);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentRegistrations_PlayerId",
                table: "TournamentRegistrations",
                column: "PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_CreatorPlayerId",
                table: "Teams",
                column: "CreatorPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_Player2PlayerId",
                table: "Teams",
                column: "Player2PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Players_Name",
                table: "Players",
                column: "Name",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_Teams_Players_CreatorPlayerId",
                table: "Teams",
                column: "CreatorPlayerId",
                principalTable: "Players",
                principalColumn: "Uid",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Teams_Players_Player2PlayerId",
                table: "Teams",
                column: "Player2PlayerId",
                principalTable: "Players",
                principalColumn: "Uid",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_TournamentRegistrations_Players_PlayerId",
                table: "TournamentRegistrations",
                column: "PlayerId",
                principalTable: "Players",
                principalColumn: "Uid",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Teams_Players_CreatorPlayerId",
                table: "Teams");

            migrationBuilder.DropForeignKey(
                name: "FK_Teams_Players_Player2PlayerId",
                table: "Teams");

            migrationBuilder.DropForeignKey(
                name: "FK_TournamentRegistrations_Players_PlayerId",
                table: "TournamentRegistrations");

            migrationBuilder.DropTable(
                name: "Players");

            migrationBuilder.DropIndex(
                name: "IX_TournamentRegistrations_PlayerId",
                table: "TournamentRegistrations");

            migrationBuilder.DropIndex(
                name: "IX_Teams_CreatorPlayerId",
                table: "Teams");

            migrationBuilder.DropIndex(
                name: "IX_Teams_Player2PlayerId",
                table: "Teams");

            migrationBuilder.AlterColumn<string>(
                name: "PlayerId",
                table: "TournamentRegistrations",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(128)",
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "Player2PlayerId",
                table: "Teams",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)");

            migrationBuilder.AlterColumn<string>(
                name: "CreatorPlayerId",
                table: "Teams",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(128)");
        }
    }
}
