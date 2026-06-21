using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daily_Bread.Migrations
{
    /// <inheritdoc />
    public partial class AddAllowsMultiplePerDayToChoreLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChoreLogs_ChoreDefinitionId_Date",
                table: "ChoreLogs");

            migrationBuilder.AddColumn<bool>(
                name: "AllowsMultiplePerDay",
                table: "ChoreLogs",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Backfill: stamp existing ChoreLog rows for WeeklyFrequency chores (ScheduleType = 1)
            // as AllowsMultiplePerDay = true, matching the rule that applies to them going
            // forward. SpecificDays rows (ScheduleType = 0) keep the column's default of false.
            // This updates column values only - no rows are inserted, deleted, or merged.
            migrationBuilder.Sql(
                """
                UPDATE "ChoreLogs" cl
                SET "AllowsMultiplePerDay" = true
                FROM "ChoreDefinitions" cd
                WHERE cl."ChoreDefinitionId" = cd."Id" AND cd."ScheduleType" = 1;
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ChoreLogs_ChoreDefinitionId_Date",
                table: "ChoreLogs",
                columns: new[] { "ChoreDefinitionId", "Date" },
                unique: true,
                filter: "\"AllowsMultiplePerDay\" = false");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChoreLogs_ChoreDefinitionId_Date",
                table: "ChoreLogs");

            migrationBuilder.DropColumn(
                name: "AllowsMultiplePerDay",
                table: "ChoreLogs");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreLogs_ChoreDefinitionId_Date",
                table: "ChoreLogs",
                columns: new[] { "ChoreDefinitionId", "Date" },
                unique: true);
        }
    }
}
