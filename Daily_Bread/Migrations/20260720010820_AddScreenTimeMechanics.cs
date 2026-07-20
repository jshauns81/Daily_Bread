using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Daily_Bread.Migrations
{
    /// <inheritdoc />
    public partial class AddScreenTimeMechanics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DailyExpectationPenalty",
                table: "FamilySettings");

            migrationBuilder.DropColumn(
                name: "WeeklyIncompletePenaltyPercent",
                table: "FamilySettings");

            migrationBuilder.DropColumn(
                name: "PenaltyValue",
                table: "ChoreDefinitions");

            migrationBuilder.AddColumn<int>(
                name: "RedemptionChoice",
                table: "ChoreLogs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "AllOrNothing",
                table: "ChoreDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Hue",
                table: "ChoreDefinitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Importance",
                table: "ChoreDefinitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "InverseFillBaselineMinutes",
                table: "ChoreDefinitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "IsInverseFill",
                table: "ChoreDefinitions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "ChoreDefinitions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "LucideIconName",
                table: "ChoreDefinitions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "WeekdayAtRiskPercent",
                table: "ChildProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "WeekdayScreenTimeHours",
                table: "ChildProfiles",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "WeekendAtRiskPercent",
                table: "ChildProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "WeekendScreenTimeHours",
                table: "ChildProfiles",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "WeeklyFixRequestAllowance",
                table: "ChildProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "WeeklyRoutinePayout",
                table: "ChildProfiles",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            // Backfill: existing zero-earn chores become Routines (Kind=1); paid chores stay Earning (Kind=0 default).
            migrationBuilder.Sql("""UPDATE "ChoreDefinitions" SET "Kind" = 1 WHERE "EarnValue" <= 0;""");

            // Backfill: give existing children the documented default screen-time settings.
            migrationBuilder.Sql("""UPDATE "ChildProfiles" SET "WeekdayScreenTimeHours" = 40, "WeekendScreenTimeHours" = 20, "WeeklyRoutinePayout" = 10, "WeekdayAtRiskPercent" = 30, "WeekendAtRiskPercent" = 50, "WeeklyFixRequestAllowance" = 3;""");

            migrationBuilder.CreateTable(
                name: "ChildWeeklyScreenTimeBudgets",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    WeekStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    WeekdayBasePoolMinutes = table.Column<int>(type: "integer", nullable: false),
                    WeekendBasePoolMinutes = table.Column<int>(type: "integer", nullable: false),
                    WeekdayMinutesLost = table.Column<int>(type: "integer", nullable: false),
                    WeekendMinutesLost = table.Column<int>(type: "integer", nullable: false),
                    InverseFillAddedMinutesPerRoutine = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChildWeeklyScreenTimeBudgets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChildWeeklyScreenTimeBudgets_ChildProfiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "ChildProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChoreScreenTimeStates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChoreDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    ConsecutiveMissWeeks = table.Column<int>(type: "integer", nullable: false),
                    LastEvaluatedWeekStart = table.Column<DateOnly>(type: "date", nullable: true),
                    CurrentWeeklyMinutesLost = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChoreScreenTimeStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChoreScreenTimeStates_ChildProfiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "ChildProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChoreScreenTimeStates_ChoreDefinitions_ChoreDefinitionId",
                        column: x => x.ChoreDefinitionId,
                        principalTable: "ChoreDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "QolShares",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChoreDefinitionId = table.Column<int>(type: "integer", nullable: false),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    SharePercent = table.Column<int>(type: "integer", nullable: false),
                    IsLocked = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_QolShares", x => x.Id);
                    table.ForeignKey(
                        name: "FK_QolShares_ChildProfiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "ChildProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_QolShares_ChoreDefinitions_ChoreDefinitionId",
                        column: x => x.ChoreDefinitionId,
                        principalTable: "ChoreDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScreenTimeEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildProfileId = table.Column<int>(type: "integer", nullable: false),
                    WeekStartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    Pool = table.Column<int>(type: "integer", nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    ChoreDefinitionId = table.Column<int>(type: "integer", nullable: true),
                    Minutes = table.Column<int>(type: "integer", nullable: false),
                    StreakMultiplier = table.Column<decimal>(type: "numeric(5,2)", precision: 5, scale: 2, nullable: true),
                    Note = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScreenTimeEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScreenTimeEntries_ChildProfiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "ChildProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ScreenTimeEntries_ChoreDefinitions_ChoreDefinitionId",
                        column: x => x.ChoreDefinitionId,
                        principalTable: "ChoreDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChildWeeklyScreenTimeBudgets_ChildProfileId_WeekStartDate",
                table: "ChildWeeklyScreenTimeBudgets",
                columns: new[] { "ChildProfileId", "WeekStartDate" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChoreScreenTimeStates_ChildProfileId",
                table: "ChoreScreenTimeStates",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreScreenTimeStates_ChoreDefinitionId_ChildProfileId",
                table: "ChoreScreenTimeStates",
                columns: new[] { "ChoreDefinitionId", "ChildProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_QolShares_ChildProfileId",
                table: "QolShares",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_QolShares_ChoreDefinitionId_ChildProfileId",
                table: "QolShares",
                columns: new[] { "ChoreDefinitionId", "ChildProfileId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScreenTimeEntries_ChildProfileId_WeekStartDate",
                table: "ScreenTimeEntries",
                columns: new[] { "ChildProfileId", "WeekStartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_ScreenTimeEntries_ChoreDefinitionId",
                table: "ScreenTimeEntries",
                column: "ChoreDefinitionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChildWeeklyScreenTimeBudgets");

            migrationBuilder.DropTable(
                name: "ChoreScreenTimeStates");

            migrationBuilder.DropTable(
                name: "QolShares");

            migrationBuilder.DropTable(
                name: "ScreenTimeEntries");

            migrationBuilder.DropColumn(
                name: "RedemptionChoice",
                table: "ChoreLogs");

            migrationBuilder.DropColumn(
                name: "AllOrNothing",
                table: "ChoreDefinitions");

            migrationBuilder.DropColumn(
                name: "Hue",
                table: "ChoreDefinitions");

            migrationBuilder.DropColumn(
                name: "Importance",
                table: "ChoreDefinitions");

            migrationBuilder.DropColumn(
                name: "InverseFillBaselineMinutes",
                table: "ChoreDefinitions");

            migrationBuilder.DropColumn(
                name: "IsInverseFill",
                table: "ChoreDefinitions");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ChoreDefinitions");

            migrationBuilder.DropColumn(
                name: "LucideIconName",
                table: "ChoreDefinitions");

            migrationBuilder.DropColumn(
                name: "WeekdayAtRiskPercent",
                table: "ChildProfiles");

            migrationBuilder.DropColumn(
                name: "WeekdayScreenTimeHours",
                table: "ChildProfiles");

            migrationBuilder.DropColumn(
                name: "WeekendAtRiskPercent",
                table: "ChildProfiles");

            migrationBuilder.DropColumn(
                name: "WeekendScreenTimeHours",
                table: "ChildProfiles");

            migrationBuilder.DropColumn(
                name: "WeeklyFixRequestAllowance",
                table: "ChildProfiles");

            migrationBuilder.DropColumn(
                name: "WeeklyRoutinePayout",
                table: "ChildProfiles");

            migrationBuilder.AddColumn<decimal>(
                name: "DailyExpectationPenalty",
                table: "FamilySettings",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "WeeklyIncompletePenaltyPercent",
                table: "FamilySettings",
                type: "numeric(5,4)",
                precision: 5,
                scale: 4,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "PenaltyValue",
                table: "ChoreDefinitions",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);
        }
    }
}
