using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Daily_Bread.Migrations
{
    /// <inheritdoc />
    public partial class AddDrivingLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DrivingGoalNightHours",
                table: "ChildProfiles",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DrivingGoalTotalHours",
                table: "ChildProfiles",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "DrivingLogEntries",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChildUserId = table.Column<string>(type: "text", nullable: false),
                    Date = table.Column<DateOnly>(type: "date", nullable: false),
                    StartTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    EndTime = table.Column<TimeOnly>(type: "time without time zone", nullable: false),
                    DurationMinutes = table.Column<int>(type: "integer", nullable: false),
                    IsNightDriving = table.Column<bool>(type: "boolean", nullable: false),
                    NightDrivingSource = table.Column<int>(type: "integer", nullable: false),
                    SupervisorUserId = table.Column<string>(type: "text", nullable: true),
                    SupervisorName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Weather = table.Column<int>(type: "integer", nullable: false),
                    RouteNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    CreatedByUserId = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<int>(type: "integer", nullable: false),
                    DecidedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DecidedByUserId = table.Column<string>(type: "text", nullable: true),
                    RejectionReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Version = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrivingLogEntries", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DrivingLogEntries_AspNetUsers_ChildUserId",
                        column: x => x.ChildUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DrivingLogEntries_AspNetUsers_CreatedByUserId",
                        column: x => x.CreatedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_DrivingLogEntries_AspNetUsers_DecidedByUserId",
                        column: x => x.DecidedByUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_DrivingLogEntries_AspNetUsers_SupervisorUserId",
                        column: x => x.SupervisorUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DrivingLogEntries_ChildUserId_Date",
                table: "DrivingLogEntries",
                columns: new[] { "ChildUserId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_DrivingLogEntries_CreatedByUserId",
                table: "DrivingLogEntries",
                column: "CreatedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DrivingLogEntries_DecidedByUserId",
                table: "DrivingLogEntries",
                column: "DecidedByUserId");

            migrationBuilder.CreateIndex(
                name: "IX_DrivingLogEntries_Status",
                table: "DrivingLogEntries",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_DrivingLogEntries_SupervisorUserId",
                table: "DrivingLogEntries",
                column: "SupervisorUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DrivingLogEntries");

            migrationBuilder.DropColumn(
                name: "DrivingGoalNightHours",
                table: "ChildProfiles");

            migrationBuilder.DropColumn(
                name: "DrivingGoalTotalHours",
                table: "ChildProfiles");
        }
    }
}
