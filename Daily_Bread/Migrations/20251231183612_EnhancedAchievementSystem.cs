using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Daily_Bread.Migrations
{
    /// <inheritdoc />
    public partial class EnhancedAchievementSystem : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "UserAchievements",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BonusDescription",
                table: "Achievements",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "BonusType",
                table: "Achievements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BonusValue",
                table: "Achievements",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedAt",
                table: "Achievements",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "HiddenHint",
                table: "Achievements",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsHidden",
                table: "Achievements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsLegendary",
                table: "Achievements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsVisibleBeforeUnlock",
                table: "Achievements",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "LockedIcon",
                table: "Achievements",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ModifiedAt",
                table: "Achievements",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ProgressTarget",
                table: "Achievements",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Rarity",
                table: "Achievements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "UnlockConditionType",
                table: "Achievements",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "UnlockConditionValue",
                table: "Achievements",
                type: "character varying(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AchievementProgress",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    AchievementId = table.Column<int>(type: "integer", nullable: false),
                    CurrentValue = table.Column<int>(type: "integer", nullable: false),
                    TargetValue = table.Column<int>(type: "integer", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StartedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    StreakAnchorDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Metadata = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AchievementProgress", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AchievementProgress_Achievements_AchievementId",
                        column: x => x.AchievementId,
                        principalTable: "Achievements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AchievementProgress_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserAchievementBonuses",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    AchievementId = table.Column<int>(type: "integer", nullable: false),
                    BonusType = table.Column<int>(type: "integer", nullable: false),
                    BonusValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RemainingUses = table.Column<int>(type: "integer", nullable: true),
                    AppliedAmount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    MaxAmount = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: true),
                    GrantedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserAchievementBonuses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserAchievementBonuses_Achievements_AchievementId",
                        column: x => x.AchievementId,
                        principalTable: "Achievements",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserAchievementBonuses_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_IsHidden",
                table: "Achievements",
                column: "IsHidden");

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_IsLegendary",
                table: "Achievements",
                column: "IsLegendary");

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_Rarity",
                table: "Achievements",
                column: "Rarity");

            migrationBuilder.CreateIndex(
                name: "IX_Achievements_UnlockConditionType",
                table: "Achievements",
                column: "UnlockConditionType");

            migrationBuilder.CreateIndex(
                name: "IX_AchievementProgress_AchievementId",
                table: "AchievementProgress",
                column: "AchievementId");

            migrationBuilder.CreateIndex(
                name: "IX_AchievementProgress_LastUpdatedAt",
                table: "AchievementProgress",
                column: "LastUpdatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_AchievementProgress_UserId_AchievementId",
                table: "AchievementProgress",
                columns: new[] { "UserId", "AchievementId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievementBonuses_AchievementId",
                table: "UserAchievementBonuses",
                column: "AchievementId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievementBonuses_BonusType",
                table: "UserAchievementBonuses",
                column: "BonusType");

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievementBonuses_ExpiresAt",
                table: "UserAchievementBonuses",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievementBonuses_IsActive",
                table: "UserAchievementBonuses",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievementBonuses_UserId",
                table: "UserAchievementBonuses",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserAchievementBonuses_UserId_IsActive_BonusType",
                table: "UserAchievementBonuses",
                columns: new[] { "UserId", "IsActive", "BonusType" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AchievementProgress");

            migrationBuilder.DropTable(
                name: "UserAchievementBonuses");

            migrationBuilder.DropIndex(
                name: "IX_Achievements_IsHidden",
                table: "Achievements");

            migrationBuilder.DropIndex(
                name: "IX_Achievements_IsLegendary",
                table: "Achievements");

            migrationBuilder.DropIndex(
                name: "IX_Achievements_Rarity",
                table: "Achievements");

            migrationBuilder.DropIndex(
                name: "IX_Achievements_UnlockConditionType",
                table: "Achievements");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "UserAchievements");

            migrationBuilder.DropColumn(
                name: "BonusDescription",
                table: "Achievements");

            migrationBuilder.DropColumn(
                name: "BonusType",
                table: "Achievements");

            migrationBuilder.DropColumn(
                name: "BonusValue",
                table: "Achievements");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                table: "Achievements");

            migrationBuilder.DropColumn(
                name: "HiddenHint",
                table: "Achievements");

            migrationBuilder.DropColumn(
                name: "IsHidden",
                table: "Achievements");

            migrationBuilder.DropColumn(
                name: "IsLegendary",
                table: "Achievements");

            migrationBuilder.DropColumn(
                name: "IsVisibleBeforeUnlock",
                table: "Achievements");

            migrationBuilder.DropColumn(
                name: "LockedIcon",
                table: "Achievements");

            migrationBuilder.DropColumn(
                name: "ModifiedAt",
                table: "Achievements");

            migrationBuilder.DropColumn(
                name: "ProgressTarget",
                table: "Achievements");

            migrationBuilder.DropColumn(
                name: "Rarity",
                table: "Achievements");

            migrationBuilder.DropColumn(
                name: "UnlockConditionType",
                table: "Achievements");

            migrationBuilder.DropColumn(
                name: "UnlockConditionValue",
                table: "Achievements");
        }
    }
}
