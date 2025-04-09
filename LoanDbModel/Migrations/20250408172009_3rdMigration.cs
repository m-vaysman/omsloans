using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LoanDbModel.Migrations
{
    /// <inheritdoc />
    public partial class _3rdMigration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedDate",
                schema: "loans",
                table: "Trades",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedDate",
                schema: "loans",
                table: "Trades",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedDate",
                schema: "loans",
                table: "TradeDocuments",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedDate",
                schema: "loans",
                table: "TradeDocuments",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedDate",
                schema: "loans",
                table: "SettledPaydownCashWires",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedDate",
                schema: "loans",
                table: "SettledPaydownCashWires",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedDate",
                schema: "loans",
                table: "Paydowns",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedDate",
                schema: "loans",
                table: "Paydowns",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedDate",
                schema: "loans",
                table: "CounterParties",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedDate",
                schema: "loans",
                table: "CounterParties",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedDate",
                schema: "loans",
                table: "Blotters",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedDate",
                schema: "loans",
                table: "Blotters",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<DateTime>(
                name: "CreatedDate",
                schema: "loans",
                table: "Accruals",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "GETUTCDATE()");

            migrationBuilder.AddColumn<DateTime>(
                name: "UpdatedDate",
                schema: "loans",
                table: "Accruals",
                type: "datetime2",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CreatedDate",
                schema: "loans",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "UpdatedDate",
                schema: "loans",
                table: "Trades");

            migrationBuilder.DropColumn(
                name: "CreatedDate",
                schema: "loans",
                table: "TradeDocuments");

            migrationBuilder.DropColumn(
                name: "UpdatedDate",
                schema: "loans",
                table: "TradeDocuments");

            migrationBuilder.DropColumn(
                name: "CreatedDate",
                schema: "loans",
                table: "SettledPaydownCashWires");

            migrationBuilder.DropColumn(
                name: "UpdatedDate",
                schema: "loans",
                table: "SettledPaydownCashWires");

            migrationBuilder.DropColumn(
                name: "CreatedDate",
                schema: "loans",
                table: "Paydowns");

            migrationBuilder.DropColumn(
                name: "UpdatedDate",
                schema: "loans",
                table: "Paydowns");

            migrationBuilder.DropColumn(
                name: "CreatedDate",
                schema: "loans",
                table: "CounterParties");

            migrationBuilder.DropColumn(
                name: "UpdatedDate",
                schema: "loans",
                table: "CounterParties");

            migrationBuilder.DropColumn(
                name: "CreatedDate",
                schema: "loans",
                table: "Blotters");

            migrationBuilder.DropColumn(
                name: "UpdatedDate",
                schema: "loans",
                table: "Blotters");

            migrationBuilder.DropColumn(
                name: "CreatedDate",
                schema: "loans",
                table: "Accruals");

            migrationBuilder.DropColumn(
                name: "UpdatedDate",
                schema: "loans",
                table: "Accruals");
        }
    }
}
