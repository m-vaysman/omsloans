using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OmsLoan.Domain.Migrations
{
    /// <inheritdoc />
    public partial class NoticeEmailMessageIdNotUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notices_EmailMessageId",
                table: "Notices");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_EmailMessageId",
                table: "Notices",
                column: "EmailMessageId",
                filter: "[EmailMessageId] IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Notices_EmailMessageId",
                table: "Notices");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_EmailMessageId",
                table: "Notices",
                column: "EmailMessageId",
                unique: true,
                filter: "[EmailMessageId] IS NOT NULL");
        }
    }
}
