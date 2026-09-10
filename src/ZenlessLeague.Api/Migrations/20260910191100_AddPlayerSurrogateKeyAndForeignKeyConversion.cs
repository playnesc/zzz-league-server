using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenlessLeague.Api.Migrations
{
    /// <inheritdoc />
    // Hand-written (not the raw EF scaffold): every "Player id" FK below is a Firebase Auth uid
    // string today, not a valid uuid literal, so a naive AlterColumn<Guid> cast would fail against
    // real data. Each column instead goes through add-nullable-Guid-column -> backfill by joining
    // the old string column to Players.Uid -> drop the old column -> rename into place. Dropping a
    // column also drops any index that references it, so every index on a converted column has to
    // be recreated explicitly afterward (EF only re-adds them for you when it used AlterColumn).
    public partial class AddPlayerSurrogateKeyAndForeignKeyConversion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Drop every FK/index that currently points at the string columns we're converting.
            migrationBuilder.DropForeignKey(name: "FK_HistoryEntries_Players_P1PlayerId", table: "HistoryEntries");
            migrationBuilder.DropForeignKey(name: "FK_HistoryEntries_Players_P2PlayerId", table: "HistoryEntries");
            migrationBuilder.DropForeignKey(name: "FK_Teams_Players_CreatorPlayerId", table: "Teams");
            migrationBuilder.DropForeignKey(name: "FK_Teams_Players_Player2PlayerId", table: "Teams");
            migrationBuilder.DropForeignKey(name: "FK_TournamentChallongeParticipants_Players_PlayerId", table: "TournamentChallongeParticipants");
            migrationBuilder.DropForeignKey(name: "FK_TournamentMatches_Players_P1PlayerId", table: "TournamentMatches");
            migrationBuilder.DropForeignKey(name: "FK_TournamentMatches_Players_P2PlayerId", table: "TournamentMatches");
            migrationBuilder.DropForeignKey(name: "FK_TournamentMatches_Players_TechLossPlayerId", table: "TournamentMatches");
            migrationBuilder.DropForeignKey(name: "FK_TournamentMatches_Players_WinnerPlayerId", table: "TournamentMatches");
            migrationBuilder.DropForeignKey(name: "FK_TournamentRegistrations_Players_PlayerId", table: "TournamentRegistrations");

            // 2. Drop TournamentRegistration.Type — fully derivable from PlayerId/TeamId, never queried.
            migrationBuilder.DropColumn(name: "Type", table: "TournamentRegistrations");

            // 3. Player.Id becomes the real surrogate PK; Uid stays as a required, unique column
            // (still the Firebase-uid join key at the HTTP boundary).
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS pgcrypto;");
            migrationBuilder.AddColumn<Guid>(name: "Id", table: "Players", type: "uuid", nullable: true);
            migrationBuilder.Sql("UPDATE \"Players\" SET \"Id\" = gen_random_uuid();");
            migrationBuilder.AlterColumn<Guid>(
                name: "Id", table: "Players", type: "uuid", nullable: false,
                oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
            migrationBuilder.DropPrimaryKey(name: "PK_Players", table: "Players");
            migrationBuilder.AddPrimaryKey(name: "PK_Players", table: "Players", column: "Id");
            migrationBuilder.CreateIndex(name: "IX_Players_Uid", table: "Players", column: "Uid", unique: true);

            // 4. Convert every dependent FK column from a Firebase uid string to Player.Id (Guid).
            ConvertUidColumnToPlayerId(migrationBuilder, "TournamentChallongeParticipants", "PlayerId");
            ConvertUidColumnToPlayerId(migrationBuilder, "TournamentRegistrations", "PlayerId");
            ConvertUidColumnToPlayerId(migrationBuilder, "TournamentMatches", "P1PlayerId");
            ConvertUidColumnToPlayerId(migrationBuilder, "TournamentMatches", "P2PlayerId");
            ConvertUidColumnToPlayerId(migrationBuilder, "TournamentMatches", "WinnerPlayerId");
            ConvertUidColumnToPlayerId(migrationBuilder, "TournamentMatches", "TechLossPlayerId");
            ConvertUidColumnToPlayerId(migrationBuilder, "Tournaments", "WinnerId");
            ConvertUidColumnToPlayerId(migrationBuilder, "HistoryEntries", "P2PlayerId");
            ConvertUidColumnToPlayerId(migrationBuilder, "HistoryEntries", "P1PlayerId");
            migrationBuilder.AlterColumn<Guid>(
                name: "P1PlayerId", table: "HistoryEntries", type: "uuid", nullable: false,
                oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);

            // Team.CreatorPlayerId/Player2PlayerId -> Player1Id/Player2Id (rename + retype together).
            ConvertUidColumnToPlayerId(migrationBuilder, "Teams", "CreatorPlayerId", newColumnName: "Player1Id");
            ConvertUidColumnToPlayerId(migrationBuilder, "Teams", "Player2PlayerId", newColumnName: "Player2Id");
            migrationBuilder.AlterColumn<Guid>(
                name: "Player1Id", table: "Teams", type: "uuid", nullable: false,
                oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);
            migrationBuilder.AlterColumn<Guid>(
                name: "Player2Id", table: "Teams", type: "uuid", nullable: false,
                oldClrType: typeof(Guid), oldType: "uuid", oldNullable: true);

            // 5. Recreate every index that was silently dropped along with its old string column.
            migrationBuilder.CreateIndex(name: "IX_HistoryEntries_P1PlayerId", table: "HistoryEntries", column: "P1PlayerId");
            migrationBuilder.CreateIndex(name: "IX_HistoryEntries_P2PlayerId", table: "HistoryEntries", column: "P2PlayerId");
            migrationBuilder.CreateIndex(name: "IX_TournamentChallongeParticipants_PlayerId", table: "TournamentChallongeParticipants", column: "PlayerId");
            migrationBuilder.CreateIndex(
                name: "IX_TournamentChallongeParticipants_TournamentId_PlayerId", table: "TournamentChallongeParticipants",
                columns: new[] { "TournamentId", "PlayerId" }, unique: true);
            migrationBuilder.CreateIndex(name: "IX_TournamentMatches_P1PlayerId", table: "TournamentMatches", column: "P1PlayerId");
            migrationBuilder.CreateIndex(name: "IX_TournamentMatches_P2PlayerId", table: "TournamentMatches", column: "P2PlayerId");
            migrationBuilder.CreateIndex(name: "IX_TournamentMatches_WinnerPlayerId", table: "TournamentMatches", column: "WinnerPlayerId");
            migrationBuilder.CreateIndex(name: "IX_TournamentMatches_TechLossPlayerId", table: "TournamentMatches", column: "TechLossPlayerId");
            migrationBuilder.CreateIndex(name: "IX_TournamentRegistrations_PlayerId", table: "TournamentRegistrations", column: "PlayerId");
            migrationBuilder.CreateIndex(
                name: "IX_TournamentRegistrations_TournamentId_PlayerId", table: "TournamentRegistrations",
                columns: new[] { "TournamentId", "PlayerId" }, unique: true, filter: "\"PlayerId\" IS NOT NULL");
            migrationBuilder.CreateIndex(name: "IX_Tournaments_WinnerId", table: "Tournaments", column: "WinnerId");
            migrationBuilder.CreateIndex(name: "IX_Tournaments_WinnerTeamId", table: "Tournaments", column: "WinnerTeamId");
            migrationBuilder.CreateIndex(name: "IX_Teams_Player1Id", table: "Teams", column: "Player1Id");
            migrationBuilder.CreateIndex(name: "IX_Teams_Player2Id", table: "Teams", column: "Player2Id");

            // 6. Re-add every FK, now pointing at Players.Id instead of Players.Uid.
            migrationBuilder.AddForeignKey(name: "FK_HistoryEntries_Players_P1PlayerId", table: "HistoryEntries", column: "P1PlayerId", principalTable: "Players", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_HistoryEntries_Players_P2PlayerId", table: "HistoryEntries", column: "P2PlayerId", principalTable: "Players", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_Teams_Players_Player1Id", table: "Teams", column: "Player1Id", principalTable: "Players", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_Teams_Players_Player2Id", table: "Teams", column: "Player2Id", principalTable: "Players", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_TournamentChallongeParticipants_Players_PlayerId", table: "TournamentChallongeParticipants", column: "PlayerId", principalTable: "Players", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_TournamentMatches_Players_P1PlayerId", table: "TournamentMatches", column: "P1PlayerId", principalTable: "Players", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_TournamentMatches_Players_P2PlayerId", table: "TournamentMatches", column: "P2PlayerId", principalTable: "Players", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_TournamentMatches_Players_TechLossPlayerId", table: "TournamentMatches", column: "TechLossPlayerId", principalTable: "Players", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_TournamentMatches_Players_WinnerPlayerId", table: "TournamentMatches", column: "WinnerPlayerId", principalTable: "Players", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_TournamentRegistrations_Players_PlayerId", table: "TournamentRegistrations", column: "PlayerId", principalTable: "Players", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_Tournaments_Players_WinnerId", table: "Tournaments", column: "WinnerId", principalTable: "Players", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
            migrationBuilder.AddForeignKey(name: "FK_Tournaments_Teams_WinnerTeamId", table: "Tournaments", column: "WinnerTeamId", principalTable: "Teams", principalColumn: "Id", onDelete: ReferentialAction.Restrict);
        }

        // Adds a nullable uuid column, backfills it from Players by joining the old string column
        // to Players.Uid, drops the old string column, then renames the new column into place.
        private static void ConvertUidColumnToPlayerId(MigrationBuilder migrationBuilder, string table, string columnName, string newColumnName = null)
        {
            var targetColumn = newColumnName ?? columnName;
            var tempColumn = targetColumn + "_new";

            migrationBuilder.AddColumn<Guid>(name: tempColumn, table: table, type: "uuid", nullable: true);
            migrationBuilder.Sql($"""
                UPDATE "{table}" t
                SET "{tempColumn}" = p."Id"
                FROM "Players" p
                WHERE t."{columnName}" = p."Uid";
                """);
            migrationBuilder.DropColumn(name: columnName, table: table);
            migrationBuilder.RenameColumn(name: tempColumn, table: table, newName: targetColumn);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new NotSupportedException(
                "This migration folds Player.Uid values into a new Guid FK graph — reversing it would " +
                "require the reverse (Id -> Uid) mapping for every row, which isn't preserved anywhere " +
                "once the old string columns are dropped. Restore from a backup taken before this " +
                "migration instead of rolling back.");
        }
    }
}
