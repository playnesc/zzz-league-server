using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenlessLeague.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTournamentsAndRegistrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TournamentRegistrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    PlayerId = table.Column<string>(type: "text", nullable: true),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: true),
                    Player1_GameUid = table.Column<string>(type: "text", nullable: false),
                    Player1_PrizeAsMoney = table.Column<bool>(type: "boolean", nullable: false),
                    Player1_PrizeUid = table.Column<string>(type: "text", nullable: true),
                    Player1_DarteNickname = table.Column<string>(type: "text", nullable: false),
                    Player1_DartePresetName = table.Column<string>(type: "text", nullable: false),
                    Player1_RosterName = table.Column<string>(type: "text", nullable: false),
                    Player2_GameUid = table.Column<string>(type: "text", nullable: true),
                    Player2_PrizeAsMoney = table.Column<bool>(type: "boolean", nullable: true),
                    Player2_PrizeUid = table.Column<string>(type: "text", nullable: true),
                    Player2_DarteNickname = table.Column<string>(type: "text", nullable: true),
                    Player2_DartePresetName = table.Column<string>(type: "text", nullable: true),
                    Player2_RosterName = table.Column<string>(type: "text", nullable: true),
                    Approved = table.Column<bool>(type: "boolean", nullable: false),
                    RegisteredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TournamentRegistrations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Tournaments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    RegistrationStartDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RegistrationEndDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TournamentStartDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TournamentEndDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MinCost = table.Column<int>(type: "integer", nullable: false),
                    MaxCost = table.Column<int>(type: "integer", nullable: false),
                    MinCharacters = table.Column<int>(type: "integer", nullable: false),
                    MinTier = table.Column<int>(type: "integer", nullable: false),
                    MaxTier = table.Column<int>(type: "integer", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    RegistrationType = table.Column<int>(type: "integer", nullable: false),
                    Visible = table.Column<bool>(type: "boolean", nullable: false),
                    BracketType = table.Column<string>(type: "text", nullable: true),
                    ChallongeTournamentId = table.Column<string>(type: "text", nullable: true),
                    ChallongeTournamentUrl = table.Column<string>(type: "text", nullable: true),
                    OverrideEloChange = table.Column<int>(type: "integer", nullable: true),
                    ConsolationMatchesTargetRank = table.Column<int>(type: "integer", nullable: true),
                    DivisionGroupId = table.Column<string>(type: "text", nullable: true),
                    DivisionIndex = table.Column<int>(type: "integer", nullable: true),
                    DiscordRoleName = table.Column<string>(type: "text", nullable: true),
                    DiscordChannelName = table.Column<string>(type: "text", nullable: true),
                    DiscordRoleId = table.Column<string>(type: "text", nullable: true),
                    DiscordChannelId = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tournaments", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TournamentRegistrations_TournamentId_PlayerId",
                table: "TournamentRegistrations",
                columns: new[] { "TournamentId", "PlayerId" },
                unique: true,
                filter: "\"PlayerId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_TournamentRegistrations_TournamentId_TeamId",
                table: "TournamentRegistrations",
                columns: new[] { "TournamentId", "TeamId" },
                unique: true,
                filter: "\"TeamId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TournamentRegistrations");

            migrationBuilder.DropTable(
                name: "Tournaments");
        }
    }
}
