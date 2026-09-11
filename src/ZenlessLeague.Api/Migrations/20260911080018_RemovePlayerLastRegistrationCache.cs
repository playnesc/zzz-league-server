using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenlessLeague.Api.Migrations
{
    /// <inheritdoc />
    public partial class RemovePlayerLastRegistrationCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastRegistration_DarteNickname",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "LastRegistration_DartePresetName",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "LastRegistration_GameUid",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "LastRegistration_HoyolabScreenshotUrl",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "LastRegistration_PrizeAsMoney",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "LastRegistration_PrizeUid",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "LastRegistration_RosterName",
                table: "Players");

            migrationBuilder.DropColumn(
                name: "LastRegistration_RosterScreenshotUrl",
                table: "Players");

            migrationBuilder.AddColumn<Guid>(
                name: "PlayerId",
                table: "ArchivedPlayerSnapshots",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ArchivedPlayerSnapshots_PlayerId",
                table: "ArchivedPlayerSnapshots",
                column: "PlayerId");

            migrationBuilder.AddForeignKey(
                name: "FK_ArchivedPlayerSnapshots_Players_PlayerId",
                table: "ArchivedPlayerSnapshots",
                column: "PlayerId",
                principalTable: "Players",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ArchivedPlayerSnapshots_Players_PlayerId",
                table: "ArchivedPlayerSnapshots");

            migrationBuilder.DropIndex(
                name: "IX_ArchivedPlayerSnapshots_PlayerId",
                table: "ArchivedPlayerSnapshots");

            migrationBuilder.DropColumn(
                name: "PlayerId",
                table: "ArchivedPlayerSnapshots");

            migrationBuilder.AddColumn<string>(
                name: "LastRegistration_DarteNickname",
                table: "Players",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRegistration_DartePresetName",
                table: "Players",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRegistration_GameUid",
                table: "Players",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRegistration_HoyolabScreenshotUrl",
                table: "Players",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "LastRegistration_PrizeAsMoney",
                table: "Players",
                type: "boolean",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRegistration_PrizeUid",
                table: "Players",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRegistration_RosterName",
                table: "Players",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "LastRegistration_RosterScreenshotUrl",
                table: "Players",
                type: "text",
                nullable: true);
        }
    }
}
