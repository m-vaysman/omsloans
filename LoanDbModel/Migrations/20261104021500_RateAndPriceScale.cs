using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LoanDbModel.Migrations
{
    /// <inheritdoc />
    public partial class RateAndPriceScale : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            AlterScale(migrationBuilder, "Accruals", "BankRate", 6, 2);
            AlterScale(migrationBuilder, "Accruals", "Spread", 6, 2);
            AlterScale(migrationBuilder, "Blotters", "Price", 6, 2);
            AlterScale(migrationBuilder, "Blotters", "Spread", 6, 2);
            AlterScale(migrationBuilder, "Trades", "Price", 6, 2);
            AlterScale(migrationBuilder, "Trades", "Spread", 6, 2);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            AlterScale(migrationBuilder, "Accruals", "BankRate", 2, 6);
            AlterScale(migrationBuilder, "Accruals", "Spread", 2, 6);
            AlterScale(migrationBuilder, "Blotters", "Price", 2, 6);
            AlterScale(migrationBuilder, "Blotters", "Spread", 2, 6);
            AlterScale(migrationBuilder, "Trades", "Price", 2, 6);
            AlterScale(migrationBuilder, "Trades", "Spread", 2, 6);
        }

        private static void AlterScale(MigrationBuilder migrationBuilder, string table, string column, int scale, int oldScale)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: column,
                schema: "loans",
                table: table,
                type: $"decimal(18,{scale})",
                precision: 18,
                scale: scale,
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: $"decimal(18,{oldScale})",
                oldPrecision: 18,
                oldScale: oldScale);
        }
    }
}
