using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddVendorBankAccount : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "VendorBankAccount",
                table: "InvoiceHeaders",
                type: "character varying(34)",
                maxLength: 34,
                nullable: true);

            migrationBuilder.Sql("""
                CREATE TABLE IF NOT EXISTS "SystemConfigs" (
                    "Key" character varying(128) NOT NULL,
                    "Value" text NOT NULL,
                    "UpdatedAt" timestamp with time zone NOT NULL,
                    CONSTRAINT "PK_SystemConfigs" PRIMARY KEY ("Key")
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SystemConfigs");

            migrationBuilder.DropColumn(
                name: "VendorBankAccount",
                table: "InvoiceHeaders");
        }
    }
}
