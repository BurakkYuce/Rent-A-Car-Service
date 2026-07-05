using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKurSistemi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KurKayitlari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Kod = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Ad = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Birim = table.Column<int>(type: "integer", nullable: false),
                    ForexAlis = table.Column<decimal>(type: "numeric(19,6)", nullable: true),
                    ForexSatis = table.Column<decimal>(type: "numeric(19,6)", nullable: true),
                    EfektifAlis = table.Column<decimal>(type: "numeric(19,6)", nullable: true),
                    EfektifSatis = table.Column<decimal>(type: "numeric(19,6)", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KurKayitlari", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SabitKurlar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kod = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Kur = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    BasTar = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BitTar = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Aktif = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SabitKurlar", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KurKayitlari_Tarih_Kod",
                table: "KurKayitlari",
                columns: new[] { "Tarih", "Kod" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SabitKurlar_TenantId_Kod",
                table: "SabitKurlar",
                columns: new[] { "TenantId", "Kod" },
                unique: true);

            // ---- KurKayitlari: PLATFORM/paylaşımlı (ulusal TCMB kuru). RLS YOK (Tenants gibi). App job
            // rate yazar → tam CRUD grant (owner-conn gerekmez). PII yok, cross-tenant yok. ----
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"KurKayitlari\" TO racar_app;");

            // ---- SabitKurlar: TENANT-OWNED (kur sabitleme; her firmanın gizli). RLS + FORCE + policy. ----
            migrationBuilder.Sql("ALTER TABLE \"SabitKurlar\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"SabitKurlar\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"SabitKurlar\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"SabitKurlar\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"SabitKurlar\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KurKayitlari");

            migrationBuilder.DropTable(
                name: "SabitKurlar");
        }
    }
}
