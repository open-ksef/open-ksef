using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddForeignServiceInvoice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BankName",
                table: "SellerProfiles",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BankSwift",
                table: "SellerProfiles",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ForeignBankAccount",
                table: "SellerProfiles",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ForeignBankName",
                table: "SellerProfiles",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ForeignBankSwift",
                table: "SellerProfiles",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerCountryCode",
                table: "IssuedInvoices",
                type: "character varying(2)",
                maxLength: 2,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerForeignId",
                table: "IssuedInvoices",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "BuyerIdType",
                table: "IssuedInvoices",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Currency",
                table: "IssuedInvoices",
                type: "character varying(3)",
                maxLength: 3,
                nullable: false,
                defaultValue: "PLN");

            migrationBuilder.AddColumn<decimal>(
                name: "ExchangeRate",
                table: "IssuedInvoices",
                type: "numeric(18,6)",
                precision: 18,
                scale: 6,
                nullable: true);

            migrationBuilder.AddColumn<DateOnly>(
                name: "ExchangeRateDate",
                table: "IssuedInvoices",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerBankName",
                table: "IssuedInvoices",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerBankSwift",
                table: "IssuedInvoices",
                type: "character varying(11)",
                maxLength: 11,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SellerIban",
                table: "IssuedInvoices",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BankName",
                table: "SellerProfiles");

            migrationBuilder.DropColumn(
                name: "BankSwift",
                table: "SellerProfiles");

            migrationBuilder.DropColumn(
                name: "ForeignBankAccount",
                table: "SellerProfiles");

            migrationBuilder.DropColumn(
                name: "ForeignBankName",
                table: "SellerProfiles");

            migrationBuilder.DropColumn(
                name: "ForeignBankSwift",
                table: "SellerProfiles");

            migrationBuilder.DropColumn(
                name: "BuyerCountryCode",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "BuyerForeignId",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "BuyerIdType",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "Currency",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRate",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "ExchangeRateDate",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "SellerBankName",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "SellerBankSwift",
                table: "IssuedInvoices");

            migrationBuilder.DropColumn(
                name: "SellerIban",
                table: "IssuedInvoices");
        }
    }
}
