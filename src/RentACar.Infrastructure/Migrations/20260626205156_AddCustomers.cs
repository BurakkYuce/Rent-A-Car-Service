using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Customers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tip = table.Column<int>(type: "integer", nullable: false),
                    Ad = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Soyad = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    TcKimlik = table.Column<string>(type: "character varying(11)", maxLength: 11, nullable: true),
                    Unvan = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    VergiDairesi = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    VergiNo = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    CepTel = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Il = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Ilce = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Adres = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Tarife = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    VadeGun = table.Column<int>(type: "integer", nullable: false),
                    RiskLimiti = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    KaraListe = table.Column<bool>(type: "boolean", nullable: false),
                    Pasif = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Customers", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_TcKimlik",
                table: "Customers",
                columns: new[] { "TenantId", "TcKimlik" },
                unique: true,
                filter: "\"TcKimlik\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_VergiNo",
                table: "Customers",
                columns: new[] { "TenantId", "VergiNo" },
                unique: true,
                filter: "\"VergiNo\" IS NOT NULL");

            // Row-Level Security (tenant izolasyonu) — Vehicles ile aynı desen.
            migrationBuilder.Sql("ALTER TABLE \"Customers\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"Customers\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"Customers\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"Customers\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"Customers\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Customers");
        }
    }
}
