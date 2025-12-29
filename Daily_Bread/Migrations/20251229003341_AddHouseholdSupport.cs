using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daily_Bread.Migrations
{
    /// <inheritdoc />
    public partial class AddHouseholdSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "HouseholdId",
                table: "AspNetUsers",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Households",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Households", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_HouseholdId",
                table: "AspNetUsers",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_Households_IsActive",
                table: "Households",
                column: "IsActive");

            migrationBuilder.AddForeignKey(
                name: "FK_AspNetUsers_Households_HouseholdId",
                table: "AspNetUsers",
                column: "HouseholdId",
                principalTable: "Households",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            // ================================================================
            // DATA BACKFILL: Create default household and assign existing users
            // ================================================================
            
            // Create a default household for existing users
            var defaultHouseholdId = Guid.NewGuid();
            migrationBuilder.InsertData(
                table: "Households",
                columns: new[] { "Id", "Name", "IsActive", "CreatedAt", "ModifiedAt" },
                values: new object[] { defaultHouseholdId, "Default Family", true, DateTime.UtcNow, null });

            // Assign all existing non-admin users to the default household
            // Admin-only users (those with only Admin role and no Parent/Child role) get null HouseholdId
            migrationBuilder.Sql($@"
                UPDATE ""AspNetUsers"" u
                SET ""HouseholdId"" = '{defaultHouseholdId}'
                WHERE u.""Id"" IN (
                    SELECT ur.""UserId""
                    FROM ""AspNetUserRoles"" ur
                    INNER JOIN ""AspNetRoles"" r ON ur.""RoleId"" = r.""Id""
                    WHERE r.""Name"" IN ('Parent', 'Child')
                )
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AspNetUsers_Households_HouseholdId",
                table: "AspNetUsers");

            migrationBuilder.DropTable(
                name: "Households");

            migrationBuilder.DropIndex(
                name: "IX_AspNetUsers_HouseholdId",
                table: "AspNetUsers");

            migrationBuilder.DropColumn(
                name: "HouseholdId",
                table: "AspNetUsers");
        }
    }
}
