using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daily_Bread.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddChildProfileAndLedgerAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LedgerTransactions_AspNetUsers_UserId",
                table: "LedgerTransactions");

            migrationBuilder.AddColumn<int>(
                name: "LedgerAccountId",
                table: "LedgerTransactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "TransferGroupId",
                table: "LedgerTransactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChildProfiles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<string>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChildProfiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChildProfiles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "LedgerAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChildProfileId = table.Column<int>(type: "INTEGER", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModifiedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LedgerAccounts", x => x.Id);
                    table.ForeignKey(
                        name: "FK_LedgerAccounts_ChildProfiles_ChildProfileId",
                        column: x => x.ChildProfileId,
                        principalTable: "ChildProfiles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_LedgerAccountId",
                table: "LedgerTransactions",
                column: "LedgerAccountId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_TransferGroupId",
                table: "LedgerTransactions",
                column: "TransferGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_ChildProfiles_IsActive",
                table: "ChildProfiles",
                column: "IsActive");

            migrationBuilder.CreateIndex(
                name: "IX_ChildProfiles_UserId",
                table: "ChildProfiles",
                column: "UserId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccounts_ChildProfileId",
                table: "LedgerAccounts",
                column: "ChildProfileId");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccounts_ChildProfileId_Name",
                table: "LedgerAccounts",
                columns: new[] { "ChildProfileId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerAccounts_IsActive",
                table: "LedgerAccounts",
                column: "IsActive");

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerTransactions_AspNetUsers_UserId",
                table: "LedgerTransactions",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerTransactions_LedgerAccounts_LedgerAccountId",
                table: "LedgerTransactions",
                column: "LedgerAccountId",
                principalTable: "LedgerAccounts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_LedgerTransactions_AspNetUsers_UserId",
                table: "LedgerTransactions");

            migrationBuilder.DropForeignKey(
                name: "FK_LedgerTransactions_LedgerAccounts_LedgerAccountId",
                table: "LedgerTransactions");

            migrationBuilder.DropTable(
                name: "LedgerAccounts");

            migrationBuilder.DropTable(
                name: "ChildProfiles");

            migrationBuilder.DropIndex(
                name: "IX_LedgerTransactions_LedgerAccountId",
                table: "LedgerTransactions");

            migrationBuilder.DropIndex(
                name: "IX_LedgerTransactions_TransferGroupId",
                table: "LedgerTransactions");

            migrationBuilder.DropColumn(
                name: "LedgerAccountId",
                table: "LedgerTransactions");

            migrationBuilder.DropColumn(
                name: "TransferGroupId",
                table: "LedgerTransactions");

            migrationBuilder.AddForeignKey(
                name: "FK_LedgerTransactions_AspNetUsers_UserId",
                table: "LedgerTransactions",
                column: "UserId",
                principalTable: "AspNetUsers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
