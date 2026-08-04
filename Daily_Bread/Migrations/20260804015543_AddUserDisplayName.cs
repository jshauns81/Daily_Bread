using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daily_Bread.Migrations
{
    /// <inheritdoc />
    public partial class AddUserDisplayName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DisplayName",
                table: "AspNetUsers",
                type: "text",
                nullable: true);

            // Children already have real names on their profiles — copy them up
            // so the canonical column starts populated. Parents have no prior
            // source; they stay null and clients fall back to the username.
            migrationBuilder.Sql("""
                UPDATE "AspNetUsers" u
                SET "DisplayName" = p."DisplayName"
                FROM "ChildProfiles" p
                WHERE p."UserId" = u."Id" AND u."DisplayName" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayName",
                table: "AspNetUsers");
        }
    }
}
