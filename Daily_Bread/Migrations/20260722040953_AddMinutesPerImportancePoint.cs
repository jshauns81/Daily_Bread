using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daily_Bread.Migrations
{
    /// <inheritdoc />
    public partial class AddMinutesPerImportancePoint : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "MinutesPerImportancePoint",
                table: "ChildProfiles",
                type: "integer",
                nullable: false,
                defaultValue: 6);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MinutesPerImportancePoint",
                table: "ChildProfiles");
        }
    }
}
