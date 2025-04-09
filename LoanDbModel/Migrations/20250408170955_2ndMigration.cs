using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LoanDbModel.Migrations
{
    /// <inheritdoc />
    public partial class _2ndMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSettled",
                schema: "loans",
                table: "Trades",
                type: "bit",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSettled",
                schema: "loans",
                table: "Trades");
        }
    }
}
