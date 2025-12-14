using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daily_Bread.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChoreScheduleOverride : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChoreScheduleOverrides",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChoreDefinitionId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Type = table.Column<int>(type: "INTEGER", nullable: false),
                    OverrideAssignedUserId = table.Column<string>(type: "TEXT", nullable: true),
                    OverrideValue = table.Column<decimal>(type: "TEXT", precision: 10, scale: 2, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "TEXT", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChoreScheduleOverrides", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChoreScheduleOverrides_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ChoreScheduleOverrides_AspNetUsers_OverrideAssignedUserId",
                        column: x => x.OverrideAssignedUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_ChoreScheduleOverrides_ChoreDefinitions_ChoreDefinitionId",
                        column: x => x.ChoreDefinitionId,
                        principalTable: "ChoreDefinitions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChoreScheduleOverrides_ChoreDefinitionId_Date",
                table: "ChoreScheduleOverrides",
                columns: new[] { "ChoreDefinitionId", "Date" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChoreScheduleOverrides_CreatedByUserId",
                table: "ChoreScheduleOverrides",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreScheduleOverrides_Date",
                table: "ChoreScheduleOverrides",
                column: "Date");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreScheduleOverrides_OverrideAssignedUserId",
                table: "ChoreScheduleOverrides",
                column: "OverrideAssignedUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ChoreScheduleOverrides_Type",
                table: "ChoreScheduleOverrides",
                column: "Type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChoreScheduleOverrides");
        }
    }
}
