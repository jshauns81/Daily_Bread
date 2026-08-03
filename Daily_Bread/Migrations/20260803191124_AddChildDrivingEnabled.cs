using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daily_Bread.Migrations
{
    /// <inheritdoc />
    public partial class AddChildDrivingEnabled : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DrivingEnabled",
                table: "ChildProfiles",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // The column defaults to false because only a teen with a permit
            // should ever see a driving log. But this replaces a rule that
            // inferred the feature from data, so switching to the explicit flag
            // would silently hide the log from every child already using one.
            // Backfill anyone the old rule would have shown it to: a configured
            // goal, or at least one entry of any status.
            migrationBuilder.Sql("""
                UPDATE "ChildProfiles"
                SET "DrivingEnabled" = TRUE
                WHERE "DrivingGoalTotalHours" IS NOT NULL
                   OR "UserId" IN (
                        SELECT DISTINCT e."ChildUserId"
                        FROM "DrivingLogEntries" e
                   );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DrivingEnabled",
                table: "ChildProfiles");
        }
    }
}
