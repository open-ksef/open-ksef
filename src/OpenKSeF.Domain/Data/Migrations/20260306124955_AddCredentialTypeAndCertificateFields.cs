using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCredentialTypeAndCertificateFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "EncryptedToken",
                table: "KSeFCredentials",
                type: "text",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "CertificateFingerprint",
                table: "KSeFCredentials",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EncryptedCertificateData",
                table: "KSeFCredentials",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "KSeFCredentials",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CertificateFingerprint",
                table: "KSeFCredentials");

            migrationBuilder.DropColumn(
                name: "EncryptedCertificateData",
                table: "KSeFCredentials");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "KSeFCredentials");

            migrationBuilder.AlterColumn<string>(
                name: "EncryptedToken",
                table: "KSeFCredentials",
                type: "text",
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }
    }
}
