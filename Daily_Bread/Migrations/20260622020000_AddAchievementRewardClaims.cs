using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Daily_Bread.Migrations
{
    /// <inheritdoc />
    public partial class AddAchievementRewardClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AchievementRewardClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserAchievementId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    AchievementId = table.Column<int>(type: "integer", nullable: false),
                    RewardType = table.Column<int>(type: "integer", nullable: false),
                    CashAmount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    ItemLabel = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ItemEstValue = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedByUserId = table.Column<string>(type: "text", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    LedgerTransactionId = table.Column<int>(type: "integer", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AchievementRewardClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AchievementRewardClaims_Achievements_AchievementId",
                        column: x => x.AchievementId,
                        principalTable: "Achievements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AchievementRewardClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AchievementRewardClaims_LedgerTransactions_LedgerTransaction",
                        column: x => x.LedgerTransactionId,
                        principalTable: "LedgerTransactions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AchievementRewardClaims_UserAchievements_UserAchievementId",
                        column: x => x.UserAchievementId,
                        principalTable: "UserAchievements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AchievementRewardClaims_AchievementId",
                table: "AchievementRewardClaims",
                column: "AchievementId");

            migrationBuilder.CreateIndex(
                name: "IX_AchievementRewardClaims_LedgerTransactionId",
                table: "AchievementRewardClaims",
                column: "LedgerTransactionId");

            migrationBuilder.CreateIndex(
                name: "IX_AchievementRewardClaims_Status",
                table: "AchievementRewardClaims",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_AchievementRewardClaims_UserAchievementId",
                table: "AchievementRewardClaims",
                column: "UserAchievementId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AchievementRewardClaims_UserId",
                table: "AchievementRewardClaims",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AchievementRewardClaims");
        }
    }
}
