using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daily_Bread.Migrations
{
    /// <inheritdoc />
    public partial class AddConcurrencyTokens : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ChoreDefinitionId",
                table: "LedgerTransactions",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "LedgerTransactions",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateOnly>(
                name: "WeekEndDate",
                table: "LedgerTransactions",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Version",
                table: "ChoreLogs",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_ChoreDefinitionId",
                table: "LedgerTransactions",
                column: "ChoreDefinitionId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_UserId_ChoreDefinitionId_WeekEndDate_Type",
                table: "LedgerTransactions",
                columns: new[] { "UserId", "ChoreDefinitionId", "WeekEndDate", "Type" });

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerTransactions_ChoreDefinitions_ChoreDefinitionId",
                table: "LedgerTransactions",
                column: "ChoreDefinitionId",
                principalTable: "ChoreDefinitions",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LedgerTransactions_ChoreDefinitions_ChoreDefinitionId",
                table: "LedgerTransactions");

            migrationBuilder.DropIndex(
                name: "IX_LedgerTransactions_ChoreDefinitionId",
                table: "LedgerTransactions");

            migrationBuilder.DropIndex(
                name: "IX_LedgerTransactions_UserId_ChoreDefinitionId_WeekEndDate_Type",
                table: "LedgerTransactions");

            migrationBuilder.DropColumn(
                name: "ChoreDefinitionId",
                table: "LedgerTransactions");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "LedgerTransactions");

            migrationBuilder.DropColumn(
                name: "WeekEndDate",
                table: "LedgerTransactions");

            migrationBuilder.DropColumn(
                name: "Version",
                table: "ChoreLogs");
        }
    }
}
