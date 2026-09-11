using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace OmsLoan.Data.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Notices",
                columns: table => new
                {
                    NoticeId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Content = table.Column<byte[]>(type: "bytea", nullable: false),
                    Sha256 = table.Column<string>(type: "character(64)", unicode: false, fixedLength: true, maxLength: 64, nullable: false),
                    Sender = table.Column<string>(type: "character varying(320)", maxLength: 320, nullable: true),
                    SentAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ReceivedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    EmailMessageId = table.Column<string>(type: "character varying(512)", unicode: false, maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Notices", x => x.NoticeId);
                });

            migrationBuilder.CreateTable(
                name: "Extractions",
                columns: table => new
                {
                    ExtractionId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    NoticeId = table.Column<int>(type: "integer", nullable: false),
                    RawJson = table.Column<string>(type: "text", nullable: false),
                    ModelName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    PromptVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    IsCurrent = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Extractions", x => x.ExtractionId);
                    table.ForeignKey(
                        name: "FK_Extractions_Notices_NoticeId",
                        column: x => x.NoticeId,
                        principalTable: "Notices",
                        principalColumn: "NoticeId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ExtractedFields",
                columns: table => new
                {
                    ExtractedFieldId = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ExtractionId = table.Column<int>(type: "integer", nullable: false),
                    FieldName = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    RawValue = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    NumericValue = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    DateValue = table.Column<DateTime>(type: "date", nullable: true),
                    Confidence = table.Column<decimal>(type: "numeric(5,4)", precision: 5, scale: 4, nullable: true),
                    CorrectedValue = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    CorrectedBy = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CorrectedAtUtc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ExtractedFields", x => x.ExtractedFieldId);
                    table.ForeignKey(
                        name: "FK_ExtractedFields_Extractions_ExtractionId",
                        column: x => x.ExtractionId,
                        principalTable: "Extractions",
                        principalColumn: "ExtractionId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ExtractedFields_ExtractionId_FieldName",
                table: "ExtractedFields",
                columns: new[] { "ExtractionId", "FieldName" });

            migrationBuilder.CreateIndex(
                name: "IX_Extractions_NoticeId_IsCurrent",
                table: "Extractions",
                columns: new[] { "NoticeId", "IsCurrent" });

            migrationBuilder.CreateIndex(
                name: "IX_Notices_EmailMessageId",
                table: "Notices",
                column: "EmailMessageId",
                filter: "\"EmailMessageId\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Notices_Sha256",
                table: "Notices",
                column: "Sha256");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ExtractedFields");

            migrationBuilder.DropTable(
                name: "Extractions");

            migrationBuilder.DropTable(
                name: "Notices");
        }
    }
}
