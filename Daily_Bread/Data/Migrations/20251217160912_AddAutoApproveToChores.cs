using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daily_Bread.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAutoApproveToChores : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "AutoApprove",
                table: "ChoreDefinitions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AutoApprove",
                table: "ChoreDefinitions");
        }
    }
}
