using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeviceTokenUniqueCascade : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DELETE FROM "DeviceTokens"
                WHERE "Id" IN (
                    SELECT "Id" FROM (
                        SELECT "Id", row_number() OVER (PARTITION BY "Token" ORDER BY "UpdatedAt" DESC, "CreatedAt" DESC, "Id" DESC) AS rn
                        FROM "DeviceTokens"
                    ) r
                    WHERE r.rn > 1
                );
                """);

            migrationBuilder.DropForeignKey(
                name: "FK_DeviceTokens_Tenants_TenantId",
                table: "DeviceTokens");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTokens_Token",
                table: "DeviceTokens",
                column: "Token",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceTokens_Tenants_TenantId",
                table: "DeviceTokens",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_DeviceTokens_Tenants_TenantId",
                table: "DeviceTokens");

            migrationBuilder.DropIndex(
                name: "IX_DeviceTokens_Token",
                table: "DeviceTokens");

            migrationBuilder.AddForeignKey(
                name: "FK_DeviceTokens_Tenants_TenantId",
                table: "DeviceTokens",
                column: "TenantId",
                principalTable: "Tenants",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }
    }
}
