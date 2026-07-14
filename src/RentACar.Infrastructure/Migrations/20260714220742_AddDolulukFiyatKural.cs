using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDolulukFiyatKural : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DolulukFiyatKurallari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ad = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    AracGrupKod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    EsikYuzde = table.Column<int>(type: "integer", nullable: false),
                    CarpanYuzde = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    GecerlilikBas = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GecerlilikBit = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Aktif = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DolulukFiyatKurallari", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DolulukFiyatKurallari_TenantId_Kod",
                table: "DolulukFiyatKurallari",
                columns: new[] { "TenantId", "Kod" },
                unique: true);

            // RLS (EF üretmez — ELLE, CLAUDE.md §5): tenant izolasyonu + FORCE; master → tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"DolulukFiyatKurallari\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"DolulukFiyatKurallari\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"DolulukFiyatKurallari\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"DolulukFiyatKurallari\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"DolulukFiyatKurallari\" TO racar_app;");

            // CHECK constraint'ler (EF üretmez — ELLE; şemadaki İLK CHECK, RLS blokları gibi emsal):
            // CarpanYuzde 0..50 SERT TAVAN (uygulama kemeri + DB pantolon askısı) ve EsikYuzde 1..100.
            migrationBuilder.Sql(
                "ALTER TABLE \"DolulukFiyatKurallari\" ADD CONSTRAINT ck_doluluk_carpan " +
                "CHECK (\"CarpanYuzde\" >= 0 AND \"CarpanYuzde\" <= 50);");
            migrationBuilder.Sql(
                "ALTER TABLE \"DolulukFiyatKurallari\" ADD CONSTRAINT ck_doluluk_esik " +
                "CHECK (\"EsikYuzde\" >= 1 AND \"EsikYuzde\" <= 100);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DolulukFiyatKurallari");
        }
    }
}
