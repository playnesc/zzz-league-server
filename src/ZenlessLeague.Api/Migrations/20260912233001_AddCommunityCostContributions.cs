using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ZenlessLeague.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddCommunityCostContributions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Contributions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorPlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    AgentId = table.Column<string>(type: "character varying(100)", nullable: true),
                    EngineId = table.Column<string>(type: "character varying(100)", nullable: true),
                    OverrideAgentId = table.Column<string>(type: "character varying(100)", nullable: true),
                    Message = table.Column<string>(type: "text", nullable: false),
                    ProposedCosts = table.Column<int[]>(type: "integer[]", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ReviewedByPlayerId = table.Column<Guid>(type: "uuid", nullable: true),
                    ReviewedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Contributions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Contributions_AgentCatalogEntries_AgentId",
                        column: x => x.AgentId,
                        principalTable: "AgentCatalogEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Contributions_AgentCatalogEntries_OverrideAgentId",
                        column: x => x.OverrideAgentId,
                        principalTable: "AgentCatalogEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Contributions_EngineCatalogEntries_EngineId",
                        column: x => x.EngineId,
                        principalTable: "EngineCatalogEntries",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Contributions_Players_AuthorPlayerId",
                        column: x => x.AuthorPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Contributions_Players_ReviewedByPlayerId",
                        column: x => x.ReviewedByPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ContributionReviews",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ContributionId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewerPlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Vote = table.Column<int>(type: "integer", nullable: false),
                    Comment = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContributionReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ContributionReviews_Contributions_ContributionId",
                        column: x => x.ContributionId,
                        principalTable: "Contributions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContributionReviews_Players_ReviewerPlayerId",
                        column: x => x.ReviewerPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ReviewComments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ReviewId = table.Column<Guid>(type: "uuid", nullable: false),
                    AuthorPlayerId = table.Column<Guid>(type: "uuid", nullable: false),
                    Text = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ReviewComments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ReviewComments_ContributionReviews_ReviewId",
                        column: x => x.ReviewId,
                        principalTable: "ContributionReviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ReviewComments_Players_AuthorPlayerId",
                        column: x => x.AuthorPlayerId,
                        principalTable: "Players",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ContributionReviews_ContributionId_ReviewerPlayerId",
                table: "ContributionReviews",
                columns: new[] { "ContributionId", "ReviewerPlayerId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ContributionReviews_ReviewerPlayerId",
                table: "ContributionReviews",
                column: "ReviewerPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Contributions_AgentId_Status",
                table: "Contributions",
                columns: new[] { "AgentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Contributions_AuthorPlayerId",
                table: "Contributions",
                column: "AuthorPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_Contributions_CreatedAt",
                table: "Contributions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_Contributions_EngineId_OverrideAgentId_Status",
                table: "Contributions",
                columns: new[] { "EngineId", "OverrideAgentId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_Contributions_OverrideAgentId",
                table: "Contributions",
                column: "OverrideAgentId");

            migrationBuilder.CreateIndex(
                name: "IX_Contributions_ReviewedByPlayerId",
                table: "Contributions",
                column: "ReviewedByPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewComments_AuthorPlayerId",
                table: "ReviewComments",
                column: "AuthorPlayerId");

            migrationBuilder.CreateIndex(
                name: "IX_ReviewComments_ReviewId",
                table: "ReviewComments",
                column: "ReviewId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ReviewComments");

            migrationBuilder.DropTable(
                name: "ContributionReviews");

            migrationBuilder.DropTable(
                name: "Contributions");
        }
    }
}
