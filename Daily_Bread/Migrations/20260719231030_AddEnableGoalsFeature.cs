using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daily_Bread.Migrations
{
    /// <inheritdoc />
    public partial class AddEnableGoalsFeature : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EnableGoals",
                table: "FamilySettings",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EnableGoals",
                table: "FamilySettings");
        }
    }
}
