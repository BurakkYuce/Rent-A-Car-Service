using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerContact : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CariYetkiliKisiler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MusteriId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sira = table.Column<int>(type: "integer", nullable: false),
                    AdSoyad = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Telefon = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Mail = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Gorev = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CariYetkiliKisiler", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CariYetkiliKisiler_Customers_MusteriId",
                        column: x => x.MusteriId,
                        principalTable: "Customers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CariYetkiliKisiler_MusteriId",
                table: "CariYetkiliKisiler",
                column: "MusteriId");

            migrationBuilder.CreateIndex(
                name: "IX_CariYetkiliKisiler_TenantId_MusteriId",
                table: "CariYetkiliKisiler",
                columns: new[] { "TenantId", "MusteriId" });

            // RLS — tenant izolasyonu (CLAUDE.md §5; EF üretmez → ELLE). Tenant-owned child, defter postalamaz
            // → immutability trigger YOK, tam CRUD grant. tenant_isolation policy set_config('app.tenant_id') okur.
            migrationBuilder.Sql("ALTER TABLE \"CariYetkiliKisiler\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"CariYetkiliKisiler\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"CariYetkiliKisiler\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"CariYetkiliKisiler\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"CariYetkiliKisiler\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CariYetkiliKisiler");
        }
    }
}
