using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmsLoan.Domain.Migrations
{
    /// <inheritdoc />
    public partial class NoticeSha256NotUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notices_Sha256",
                table: "Notices");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_Sha256",
                table: "Notices",
                column: "Sha256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notices_Sha256",
                table: "Notices");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_Sha256",
                table: "Notices",
                column: "Sha256",
                unique: true);
        }
    }
}
