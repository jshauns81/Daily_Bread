using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Daily_Bread.Data.Migrations
{
    /// <inheritdoc />
    public partial class NullableChoreLogId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LedgerTransactions_ChoreLogId",
                table: "LedgerTransactions");

            migrationBuilder.AlterColumn<int>(
                name: "ChoreLogId",
                table: "LedgerTransactions",
                type: "INTEGER",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "INTEGER");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_ChoreLogId",
                table: "LedgerTransactions",
                column: "ChoreLogId",
                unique: true,
                filter: "[ChoreLogId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_Type",
                table: "LedgerTransactions",
                column: "Type");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_LedgerTransactions_ChoreLogId",
                table: "LedgerTransactions");

            migrationBuilder.DropIndex(
                name: "IX_LedgerTransactions_Type",
                table: "LedgerTransactions");

            migrationBuilder.AlterColumn<int>(
                name: "ChoreLogId",
                table: "LedgerTransactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "INTEGER",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_LedgerTransactions_ChoreLogId",
                table: "LedgerTransactions",
                column: "ChoreLogId",
                unique: true);
        }
    }
}
