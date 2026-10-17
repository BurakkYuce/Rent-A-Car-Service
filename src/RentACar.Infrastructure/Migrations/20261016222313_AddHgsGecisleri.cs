using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHgsGecisleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HgsGecisleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Plaka = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    EtiketNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AracSinifi = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    GirisIstasyonu = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    CikisIstasyonu = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IstasyonAnahtari = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    GirisZamani = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GecisZamani = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Tutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    RentalId = table.Column<Guid>(type: "uuid", nullable: true),
                    EslesmeNotu = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    HizmetOrani = table.Column<decimal>(type: "numeric(9,4)", nullable: true),
                    YansitilanTutar = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    YansitmaZamani = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    YansitilanCariId = table.Column<Guid>(type: "uuid", nullable: true),
                    IceAktarimId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kaynak = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    DosyaAdi = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HgsGecisleri", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HgsGecisleri_DogalAnahtar",
                table: "HgsGecisleri",
                columns: new[] { "TenantId", "Plaka", "GecisZamani", "IstasyonAnahtari", "Tutar" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HgsGecisleri_TenantId_Durum_GecisZamani",
                table: "HgsGecisleri",
                columns: new[] { "TenantId", "Durum", "GecisZamani" });

            migrationBuilder.CreateIndex(
                name: "IX_HgsGecisleri_TenantId_IceAktarimId",
                table: "HgsGecisleri",
                columns: new[] { "TenantId", "IceAktarimId" });

            migrationBuilder.CreateIndex(
                name: "IX_HgsGecisleri_TenantId_RentalId",
                table: "HgsGecisleri",
                columns: new[] { "TenantId", "RentalId" });

            // ---- Tutarlılık (EF üretmez): geçiş tutarı pozitif; yansıtılmış satır yansıtma alanlarını taşır.
            migrationBuilder.Sql(
                "ALTER TABLE \"HgsGecisleri\" ADD CONSTRAINT \"CK_HgsGecisleri_Tutar\" CHECK (\"Tutar\" > 0);");
            migrationBuilder.Sql(
                "ALTER TABLE \"HgsGecisleri\" ADD CONSTRAINT \"CK_HgsGecisleri_Yansitma\" CHECK (\"Durum\" <> 3 OR " +
                "(\"YansitilanTutar\" IS NOT NULL AND \"YansitilanCariId\" IS NOT NULL AND \"HizmetOrani\" IS NOT NULL " +
                "AND \"YansitmaZamani\" IS NOT NULL AND \"RentalId\" IS NOT NULL));");

            // ---- Yansıtılmış geçiş DEĞİŞMEZ (Durum = 3 → UPDATE/DELETE yasak). Yansıtılmamış satır güncellenebilir
            // (yeniden eşleştirme, yansıtma geçişi) — bu yüzden rc_prevent_mutation değil, duruma bakan ayrı trigger.
            migrationBuilder.Sql(
                "CREATE OR REPLACE FUNCTION rc_hgs_gecis_degismez() RETURNS trigger AS $$ " +
                "BEGIN IF OLD.\"Durum\" = 3 THEN " +
                "RAISE EXCEPTION 'Yansıtılmış HGS geçişi değiştirilemez (UPDATE/DELETE yasak): %', OLD.\"Id\"; END IF; " +
                "IF TG_OP = 'DELETE' THEN RETURN OLD; END IF; RETURN NEW; END; $$ LANGUAGE plpgsql;");
            migrationBuilder.Sql(
                "CREATE TRIGGER hgs_gecis_degismez BEFORE UPDATE OR DELETE ON \"HgsGecisleri\" " +
                "FOR EACH ROW EXECUTE FUNCTION rc_hgs_gecis_degismez();");

            // ---- RLS (tenant izolasyonu). EF ÜRETMEZ, elle eklenir (CLAUDE.md §5-4). Mali belge değil (durum
            // güncellenir) → tam CRUD grant; yansıtılmış satırı yukarıdaki trigger korur.
            migrationBuilder.Sql("ALTER TABLE \"HgsGecisleri\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"HgsGecisleri\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"HgsGecisleri\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"HgsGecisleri\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"HgsGecisleri\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS hgs_gecis_degismez ON \"HgsGecisleri\";");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS rc_hgs_gecis_degismez();");
            migrationBuilder.DropTable(
                name: "HgsGecisleri");
        }
    }
}
