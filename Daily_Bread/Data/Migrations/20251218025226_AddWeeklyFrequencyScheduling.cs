using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daily_Bread.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddWeeklyFrequencyScheduling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ScheduleType",
                table: "ChoreDefinitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WeeklyTargetCount",
                table: "ChoreDefinitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScheduleType",
                table: "ChoreDefinitions");

            migrationBuilder.DropColumn(
                name: "WeeklyTargetCount",
                table: "ChoreDefinitions");
        }
    }
}
