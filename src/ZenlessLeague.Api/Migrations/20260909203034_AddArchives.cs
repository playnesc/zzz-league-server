using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenlessLeague.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddArchives : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Archives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SeasonName = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Archives", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ArchivedPlayerSnapshots",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ArchiveId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Elo = table.Column<int>(type: "integer", nullable: false),
                    IsMidConfirmed = table.Column<bool>(type: "boolean", nullable: false),
                    IsHighConfirmed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ArchivedPlayerSnapshots", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ArchivedPlayerSnapshots_Archives_ArchiveId",
                        column: x => x.ArchiveId,
                        principalTable: "Archives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ArchivedPlayerSnapshots_ArchiveId",
                table: "ArchivedPlayerSnapshots",
                column: "ArchiveId");

            migrationBuilder.CreateIndex(
                name: "IX_Archives_SeasonName",
                table: "Archives",
                column: "SeasonName",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ArchivedPlayerSnapshots");

            migrationBuilder.DropTable(
                name: "Archives");
        }
    }
}
