using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenlessLeague.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddHistoryEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HistoryEntries",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    P1PlayerId = table.Column<string>(type: "character varying(128)", nullable: false),
                    P1Change = table.Column<int>(type: "integer", nullable: false),
                    P2PlayerId = table.Column<string>(type: "character varying(128)", nullable: true),
                    P2Change = table.Column<int>(type: "integer", nullable: true),
                    TournamentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ResultP1 = table.Column<string>(type: "text", nullable: true),
                    ResultP2 = table.Column<string>(type: "text", nullable: true),
                    ResultScreenshotUrl = table.Column<string>(type: "text", nullable: true),
                    Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HistoryEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HistoryEntries_Players_P1PlayerId",
                        column: x => x.P1PlayerId,
                        principalTable: "Players",
                        principalColumn: "Uid",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HistoryEntries_Players_P2PlayerId",
                        column: x => x.P2PlayerId,
                        principalTable: "Players",
                        principalColumn: "Uid",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_HistoryEntries_Tournaments_TournamentId",
                        column: x => x.TournamentId,
                        principalTable: "Tournaments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HistoryEntries_P1PlayerId",
                table: "HistoryEntries",
                column: "P1PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoryEntries_P2PlayerId",
                table: "HistoryEntries",
                column: "P2PlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_HistoryEntries_TournamentId",
                table: "HistoryEntries",
                column: "TournamentId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HistoryEntries");
        }
    }
}
