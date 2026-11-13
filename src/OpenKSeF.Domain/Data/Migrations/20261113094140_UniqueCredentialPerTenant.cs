using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class UniqueCredentialPerTenant : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "KSeFCredentials"
                WHERE "Id" IN (
                    SELECT "Id" FROM (
                        SELECT "Id", row_number() OVER (PARTITION BY "TenantId" ORDER BY "UpdatedAt" DESC, "Id" DESC) AS rn
                        FROM "KSeFCredentials"
                    ) r
                    WHERE r.rn > 1
                );
                """);

            migrationBuilder.DropIndex(
                name: "IX_KSeFCredentials_TenantId",
                table: "KSeFCredentials");

            migrationBuilder.CreateIndex(
                name: "IX_KSeFCredentials_TenantId",
                table: "KSeFCredentials",
                column: "TenantId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_KSeFCredentials_TenantId",
                table: "KSeFCredentials");

            migrationBuilder.CreateIndex(
                name: "IX_KSeFCredentials_TenantId",
                table: "KSeFCredentials",
                column: "TenantId");
        }
    }
}
