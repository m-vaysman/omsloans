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
                name: "loan");

            migrationBuilder.CreateTable(
                name: "Accruals",
                columns: table => new
                {
                    AccrualId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FromDate = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accruals", x => x.AccrualId);
                });

            migrationBuilder.CreateTable(
                name: "CounterParties",
                schema: "loan",
                columns: table => new
                {
                    CounterPartyId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CounterPartyName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CounterPartyCode = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DomicileCountry = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CounterParties", x => x.CounterPartyId);
                });

            migrationBuilder.CreateTable(
                name: "Blotters",
                schema: "loan",
                columns: table => new
                {
                    BlotterId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Ticker = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CounterPartyId = table.Column<int>(type: "int", nullable: false),
                    CUSIP = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    TradeDate = table.Column<DateTime>(type: "datetime2", nullable: false),
                    BuySell = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Price = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GlobalCommitment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Notional = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TradeAcct = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Document = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Ticket = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Spread = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Blotters", x => x.BlotterId);
                    table.ForeignKey(
                        name: "FK_Blotters_CounterParties_CounterPartyId",
                        column: x => x.CounterPartyId,
                        principalSchema: "loan",
                        principalTable: "CounterParties",
                        principalColumn: "CounterPartyId");
                });

            migrationBuilder.CreateTable(
                name: "Trades",
                schema: "loan",
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
                    Price = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    GlobalCommitment = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Notional = table.Column<decimal>(type: "money", nullable: false),
                    TermUnfundedAmount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    CommitmentReduction = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    FeesCosts = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    DelayCompensation = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    InterestReceived = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    AdditionalCredit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    EconomicBenefit = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    TradeAcct = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Strategy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    SubStrategy = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Currency = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Spread = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Trades", x => x.TradeId);
                    table.ForeignKey(
                        name: "FK_Trades_CounterParties_CounterPartyId",
                        column: x => x.CounterPartyId,
                        principalSchema: "loan",
                        principalTable: "CounterParties",
                        principalColumn: "CounterPartyId");
                });

            migrationBuilder.CreateTable(
                name: "Paydowns",
                schema: "loan",
                columns: table => new
                {
                    PaydownId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    TradeId = table.Column<int>(type: "int", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Notice = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    ProRataShare = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Paydowns", x => x.PaydownId);
                    table.ForeignKey(
                        name: "FK_Paydowns_Trades_TradeId",
                        column: x => x.TradeId,
                        principalSchema: "loan",
                        principalTable: "Trades",
                        principalColumn: "TradeId");
                });

            migrationBuilder.CreateTable(
                name: "SettledPaydownCashWires",
                schema: "loan",
                columns: table => new
                {
                    SettledPaydownCashWireId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PaydownId = table.Column<int>(type: "int", nullable: false),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Date = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Note = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettledPaydownCashWires", x => x.SettledPaydownCashWireId);
                    table.ForeignKey(
                        name: "FK_SettledPaydownCashWires_Paydowns_PaydownId",
                        column: x => x.PaydownId,
                        principalSchema: "loan",
                        principalTable: "Paydowns",
                        principalColumn: "PaydownId");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Blotters_CounterPartyId",
                schema: "loan",
                table: "Blotters",
                column: "CounterPartyId");

            migrationBuilder.CreateIndex(
                name: "IX_Paydowns_TradeId",
                schema: "loan",
                table: "Paydowns",
                column: "TradeId");

            migrationBuilder.CreateIndex(
                name: "IX_SettledPaydownCashWires_PaydownId",
                schema: "loan",
                table: "SettledPaydownCashWires",
                column: "PaydownId");

            migrationBuilder.CreateIndex(
                name: "IX_Trades_CounterPartyId",
                schema: "loan",
                table: "Trades",
                column: "CounterPartyId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Accruals");

            migrationBuilder.DropTable(
                name: "Blotters",
                schema: "loan");

            migrationBuilder.DropTable(
                name: "SettledPaydownCashWires",
                schema: "loan");

            migrationBuilder.DropTable(
                name: "Paydowns",
                schema: "loan");

            migrationBuilder.DropTable(
                name: "Trades",
                schema: "loan");

            migrationBuilder.DropTable(
                name: "CounterParties",
                schema: "loan");
        }
    }
}
