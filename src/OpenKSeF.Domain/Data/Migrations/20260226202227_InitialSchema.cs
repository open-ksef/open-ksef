using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OpenKSeF.Domain.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Tenants",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Nip = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Tenants", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "DeviceTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Platform = table.Column<int>(type: "integer", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceTokens_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceHeaders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    KSeFInvoiceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    KSeFReferenceNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    VendorName = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                    VendorNip = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    AmountGross = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false, defaultValue: "PLN"),
                    IssueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    SaleDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    FirstSeenAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceHeaders", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceHeaders_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "KSeFCredentials",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EncryptedToken = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KSeFCredentials", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KSeFCredentials_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SyncStates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LastSyncedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    Watermark = table.Column<string>(type: "text", nullable: true),
                    LastSuccessfulSync = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncStates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SyncStates_Tenants_TenantId",
                        column: x => x.TenantId,
                        principalTable: "Tenants",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTokens_TenantId",
                table: "DeviceTokens",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_DeviceTokens_UserId",
                table: "DeviceTokens",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeaders_IssueDate",
                table: "InvoiceHeaders",
                column: "IssueDate");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceHeaders_TenantId_KSeFInvoiceNumber",
                table: "InvoiceHeaders",
                columns: new[] { "TenantId", "KSeFInvoiceNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KSeFCredentials_TenantId",
                table: "KSeFCredentials",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_SyncStates_TenantId",
                table: "SyncStates",
                column: "TenantId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Tenants_UserId_Nip",
                table: "Tenants",
                columns: new[] { "UserId", "Nip" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceTokens");

            migrationBuilder.DropTable(
                name: "InvoiceHeaders");

            migrationBuilder.DropTable(
                name: "KSeFCredentials");

            migrationBuilder.DropTable(
                name: "SyncStates");

            migrationBuilder.DropTable(
                name: "Tenants");
        }
    }
}
