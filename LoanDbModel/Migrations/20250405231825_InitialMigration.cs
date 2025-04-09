using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LoanDbModel.Migrations
{
    /// <inheritdoc />
    public partial class InitialMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "loans");

            migrationBuilder.CreateTable(
                name: "CounterParties",
                schema: "loans",
                columns: table => new
                {
                    CounterPartyId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CounterPartyCode = table.Column<string>(type: "varchar(50)", nullable: false),
                    CounterPartyName = table.Column<string>(type: "varchar(500)", nullable: false),
                    DomicileCountry = table.Column<string>(type: "varchar(50)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CounterParties", x => x.CounterPartyId);
                });

            migrationBuilder.CreateTable(
                name: "Blotters",
                schema: "loans",
                columns: table => new
                {
                    BlotterId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BuySell = table.Column<string>(type: "varchar(1)", nullable: false),
                    CounterPartyId = table.Column<int>(type: "int", nullable: false),
                    CUSIP = table.Column<string>(type: "varchar(50)", nullable: true),
                    Document = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    GlobalCommitment = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Notional = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Spread = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Ticker = table.Column<string>(type: "varchar(50)", nullable: false),
                    Ticket = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TradeAcct = table.Column<string>(type: "varchar(50)", nullable: false),
                    TradeDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blotters", x => x.BlotterId);
                    table.ForeignKey(
                        name: "FK_Blotters_CounterParties_CounterPartyId",
                        column: x => x.CounterPartyId,
                        principalSchema: "loans",
                        principalTable: "CounterParties",
                        principalColumn: "CounterPartyId");
                });

            migrationBuilder.CreateTable(
                name: "Trades",
                schema: "loans",
                columns: table => new
                {
                    TradeId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CounterPartyId = table.Column<int>(type: "int", nullable: false),
                    Ticker = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CUSIP = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TradeDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SettlementDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BuySell = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Price = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    GlobalCommitment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Notional = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TermUnfundedAmount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    CommitmentReduction = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    FeesCosts = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    DelayCompensation = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    InterestReceived = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    AdditionalCredit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    EconomicBenefit = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    TradeAcct = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Strategy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubStrategy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Spread = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.TradeId);
                    table.ForeignKey(
                        name: "FK_Trades_CounterParties_CounterPartyId",
                        column: x => x.CounterPartyId,
                        principalSchema: "loans",
                        principalTable: "CounterParties",
                        principalColumn: "CounterPartyId");
                });

            migrationBuilder.CreateTable(
                name: "Accruals",
                schema: "loans",
                columns: table => new
                {
                    AccrualId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ParentAccrualId = table.Column<int>(type: "int", nullable: true),
                    AccrualCode = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false, computedColumnSql: "'ACR' + RIGHT('00000' + CAST([AccrualId] AS VARCHAR(5)), 5)", stored: true),
                    TradeId = table.Column<int>(type: "int", nullable: false),
                    Notional = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FromDate = table.Column<DateOnly>(type: "date", nullable: false),
                    ToDate = table.Column<DateOnly>(type: "date", nullable: false),
                    BankRate = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Spread = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Act = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accruals", x => x.AccrualId);
                    table.ForeignKey(
                        name: "FK_Accruals_Accruals_ParentAccrualId",
                        column: x => x.ParentAccrualId,
                        principalSchema: "loans",
                        principalTable: "Accruals",
                        principalColumn: "AccrualId");
                    table.ForeignKey(
                        name: "FK_Accruals_Trades_TradeId",
                        column: x => x.TradeId,
                        principalSchema: "loans",
                        principalTable: "Trades",
                        principalColumn: "TradeId");
                });

            migrationBuilder.CreateTable(
                name: "Paydowns",
                schema: "loans",
                columns: table => new
                {
                    PaydownId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Notice = table.Column<string>(type: "varchar(500)", nullable: true),
                    ProRataShare = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Paydowns", x => x.PaydownId);
                    table.ForeignKey(
                        name: "FK_Paydowns_Trades_TradeId",
                        column: x => x.TradeId,
                        principalSchema: "loans",
                        principalTable: "Trades",
                        principalColumn: "TradeId");
                });

            migrationBuilder.CreateTable(
                name: "TradeDocuments",
                schema: "loans",
                columns: table => new
                {
                    TradeDocumentId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "varchar(500)", nullable: false),
                    ContentType = table.Column<string>(type: "varchar(100)", nullable: false),
                    Data = table.Column<byte[]>(type: "varbinary(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TradeDocuments", x => x.TradeDocumentId);
                    table.ForeignKey(
                        name: "FK_TradeDocuments_Trades_TradeId",
                        column: x => x.TradeId,
                        principalSchema: "loans",
                        principalTable: "Trades",
                        principalColumn: "TradeId");
                });

            migrationBuilder.CreateTable(
                name: "SettledPaydownCashWires",
                schema: "loans",
                columns: table => new
                {
                    SettledPaydownCashWireId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", precision: 18, scale: 2, nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    PaydownId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettledPaydownCashWires", x => x.SettledPaydownCashWireId);
                    table.ForeignKey(
                        name: "FK_SettledPaydownCashWires_Paydowns_PaydownId",
                        column: x => x.PaydownId,
                        principalSchema: "loans",
                        principalTable: "Paydowns",
                        principalColumn: "PaydownId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Accruals_AccrualCode",
                schema: "loans",
                table: "Accruals",
                column: "AccrualCode",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Accruals_ParentAccrualId",
                schema: "loans",
                table: "Accruals",
                column: "ParentAccrualId");

            migrationBuilder.CreateIndex(
                name: "IX_Accruals_TradeId",
                schema: "loans",
                table: "Accruals",
                column: "TradeId");

            migrationBuilder.CreateIndex(
                name: "IX_Blotters_CounterPartyId",
                schema: "loans",
                table: "Blotters",
                column: "CounterPartyId");

            migrationBuilder.CreateIndex(
                name: "IX_Paydowns_TradeId",
                schema: "loans",
                table: "Paydowns",
                column: "TradeId");

            migrationBuilder.CreateIndex(
                name: "IX_SettledPaydownCashWires_PaydownId",
                schema: "loans",
                table: "SettledPaydownCashWires",
                column: "PaydownId");

            migrationBuilder.CreateIndex(
                name: "IX_TradeDocuments_TradeId",
                schema: "loans",
                table: "TradeDocuments",
                column: "TradeId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_CounterPartyId",
                schema: "loans",
                table: "Trades",
                column: "CounterPartyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Accruals",
                schema: "loans");

            migrationBuilder.DropTable(
                name: "Blotters",
                schema: "loans");

            migrationBuilder.DropTable(
                name: "SettledPaydownCashWires",
                schema: "loans");

            migrationBuilder.DropTable(
                name: "TradeDocuments",
                schema: "loans");

            migrationBuilder.DropTable(
                name: "Paydowns",
                schema: "loans");

            migrationBuilder.DropTable(
                name: "Trades",
                schema: "loans");

            migrationBuilder.DropTable(
                name: "CounterParties",
                schema: "loans");
        }
    }
}
