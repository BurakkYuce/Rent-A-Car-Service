using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCezaDerinlikVeKismiOdeme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CepTel",
                table: "Penalties",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IslemSube",
                table: "Penalties",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Kalan",
                table: "Penalties",
                type: "numeric(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "MakbuzNo",
                table: "Penalties",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OdenenTutar",
                table: "Penalties",
                type: "numeric(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OdenmeTarihi",
                table: "Penalties",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Saat",
                table: "Penalties",
                type: "character varying(8)",
                maxLength: 8,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Yer",
                table: "Penalties",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Penalties_TenantId_Id",
                table: "Penalties",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "PenaltySatirlari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PenaltyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sira = table.Column<int>(type: "integer", nullable: false),
                    Tutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Sebep = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Odenen = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Kalan = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenaltySatirlari", x => x.Id);
                    table.UniqueConstraint("AK_PenaltySatirlari_TenantId_Id", x => new { x.TenantId, x.Id });
                    table.ForeignKey(
                        name: "FK_PenaltySatirlari_Penalties_TenantId_PenaltyId",
                        columns: x => new { x.TenantId, x.PenaltyId },
                        principalTable: "Penalties",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PenaltyOdemeleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PenaltyId = table.Column<Guid>(type: "uuid", nullable: false),
                    SatirId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sira = table.Column<int>(type: "integer", nullable: false),
                    Tutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    KalanSonrasi = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Hesap = table.Column<int>(type: "integer", nullable: false),
                    Anahtar = table.Column<string>(type: "character varying(160)", maxLength: 160, nullable: false),
                    IslemAnahtari = table.Column<Guid>(type: "uuid", nullable: true),
                    KasaKodu = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    HesapNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MakbuzNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IslemYapan = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PenaltyOdemeleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PenaltyOdemeleri_PenaltySatirlari_TenantId_SatirId",
                        columns: x => new { x.TenantId, x.SatirId },
                        principalTable: "PenaltySatirlari",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Penalties_TenantId_MakbuzNo",
                table: "Penalties",
                columns: new[] { "TenantId", "MakbuzNo" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountLedgerEntries_CezaOdeme_Idem",
                table: "AccountLedgerEntries",
                columns: new[] { "TenantId", "SourceType", "SourceId", "Direction" },
                unique: true,
                filter: "\"SourceType\" = 'CezaOdeme'");

            migrationBuilder.CreateIndex(
                name: "IX_PenaltyOdemeleri_TenantId_Anahtar",
                table: "PenaltyOdemeleri",
                columns: new[] { "TenantId", "Anahtar" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PenaltyOdemeleri_TenantId_IslemAnahtari",
                table: "PenaltyOdemeleri",
                columns: new[] { "TenantId", "IslemAnahtari" },
                unique: true,
                filter: "\"IslemAnahtari\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PenaltyOdemeleri_TenantId_PenaltyId",
                table: "PenaltyOdemeleri",
                columns: new[] { "TenantId", "PenaltyId" });

            migrationBuilder.CreateIndex(
                name: "IX_PenaltyOdemeleri_TenantId_SatirId",
                table: "PenaltyOdemeleri",
                columns: new[] { "TenantId", "SatirId" });

            migrationBuilder.CreateIndex(
                name: "IX_PenaltySatirlari_TenantId_PenaltyId_Sira",
                table: "PenaltySatirlari",
                columns: new[] { "TenantId", "PenaltyId", "Sira" },
                unique: true);

            // ---- RLS (EF ÜRETMEZ — CLAUDE.md §5) ----
            foreach (var t in new[] { "PenaltySatirlari", "PenaltyOdemeleri" })
            {
                migrationBuilder.Sql($"ALTER TABLE \"{t}\" ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE \"{t}\" FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON \"{t}\";");
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON \"{t}\" " +
                    "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                    "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            }
            // Kalemler operasyoneldir (Odenen/Kalan güncellenir) → SELECT/INSERT/UPDATE.
            // DELETE YOK — Penalties'in kendisinde de DELETE yetkisi yok (kayıt silinmez, iptal edilir);
            // silme yetkisi vermek ödemesi olan bir kalemi ekrandan yok etmenin yolunu açardı.
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"PenaltySatirlari\" TO racar_app;");
            // Ödeme satırları MALİ BELGEDİR: yalnız SELECT + INSERT. UPDATE/DELETE yetkisi YOK →
            // uygulama bir ödemeyi ne değiştirebilir ne silebilir (FAZ-14 MtvOdemeleri deseni).
            migrationBuilder.Sql("GRANT SELECT, INSERT ON \"PenaltyOdemeleri\" TO racar_app;");
            // Grant TEK BAŞINA yetmez — racar_owner (migrator/ops betikleri) hâlâ UPDATE/DELETE
            // edebilirdi ve o an Kalan ile Σ ödeme sessizce ayrışırdı. DB seviyesinde değişmezlik.
            migrationBuilder.Sql(
                "CREATE TRIGGER penaltyodemeleri_immutable BEFORE UPDATE OR DELETE ON \"PenaltyOdemeleri\" " +
                "FOR EACH ROW EXECUTE FUNCTION rc_prevent_mutation();");

            // ---- BACKFILL ----
            // TUZAK (FAZ-14'te ampirik doğrulandı): FORCE ROW LEVEL SECURITY açık ve racar_owner'da
            // BYPASSRLS YOK. Düz UPDATE/SELECT policy'yi boş app.tenant_id ile değerlendirip SIFIR
            // satır görür → backfill SESSİZCE hiçbir şey yapmaz. Bu yüzden NO FORCE / FORCE
            // parantezine alınıyor (aynı transaction).
            //
            // ANLAMA GÖRE BACKFILL: EF'in verdiği defaultValue 0, mevcut ÖDENMEMİŞ cezalara
            // "tamamı ödendi" (Kalan=0) anlamı yüklerdi — bu faz için en kritik tuzak.
            //   Durum = 2 (Odendi)  → Odenen = Tutar, Kalan = 0
            //   diğer               → Odenen = 0,     Kalan = Tutar
            migrationBuilder.Sql("ALTER TABLE \"Penalties\" NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"PenaltySatirlari\" NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "UPDATE \"Penalties\" SET " +
                "\"OdenenTutar\" = CASE WHEN \"Durum\" = 2 THEN \"Tutar\" ELSE 0 END, " +
                "\"Kalan\" = CASE WHEN \"Durum\" = 2 THEN 0 ELSE \"Tutar\" END, " +
                "\"OdenmeTarihi\" = CASE WHEN \"Durum\" = 2 THEN COALESCE(\"UpdatedAtUtc\", \"CreatedAtUtc\") ELSE NULL END;");
            // Her cezanın EN AZ BİR kalemi olmalı — eski tek-tutarlı kayıtlar 1. kalem olarak
            // maddeleştirilir. Böylece "kalemsiz ceza" özel durumu hiç doğmaz ve
            // Tutar == Σ Kalem.Tutar değişmezi geçmiş veriye de uygulanır.
            migrationBuilder.Sql(
                "INSERT INTO \"PenaltySatirlari\" " +
                "(\"Id\",\"TenantId\",\"PenaltyId\",\"Sira\",\"Tutar\",\"Sebep\",\"Odenen\",\"Kalan\",\"CreatedAtUtc\",\"UpdatedAtUtc\") " +
                "SELECT gen_random_uuid(), p.\"TenantId\", p.\"Id\", 1, p.\"Tutar\", p.\"Sebep\", " +
                "CASE WHEN p.\"Durum\" = 2 THEN p.\"Tutar\" ELSE 0 END, " +
                "CASE WHEN p.\"Durum\" = 2 THEN 0 ELSE p.\"Tutar\" END, " +
                "p.\"CreatedAtUtc\", NULL FROM \"Penalties\" p " +
                "WHERE NOT EXISTS (SELECT 1 FROM \"PenaltySatirlari\" s WHERE s.\"PenaltyId\" = p.\"Id\");");
            migrationBuilder.Sql("ALTER TABLE \"PenaltySatirlari\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"Penalties\" FORCE ROW LEVEL SECURITY;");

            // ---- DB savunması (uygulama zaten reddediyor; DB kendi başına da savunsun) ----
            migrationBuilder.Sql(
                "ALTER TABLE \"PenaltyOdemeleri\" ADD CONSTRAINT \"CK_PenaltyOdemeleri_TutarPozitif\" CHECK (\"Tutar\" > 0);");
            migrationBuilder.Sql(
                "ALTER TABLE \"PenaltySatirlari\" ADD CONSTRAINT \"CK_PenaltySatirlari_Tutarlar\" " +
                "CHECK (\"Tutar\" > 0 AND \"Odenen\" >= 0 AND \"Kalan\" >= 0 AND \"Odenen\" <= \"Tutar\");");
            migrationBuilder.Sql(
                "ALTER TABLE \"Penalties\" ADD CONSTRAINT \"CK_Penalties_Tutarlar\" " +
                "CHECK (\"OdenenTutar\" >= 0 AND \"Kalan\" >= 0 AND \"OdenenTutar\" <= \"Tutar\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE \"Penalties\" DROP CONSTRAINT IF EXISTS \"CK_Penalties_Tutarlar\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS penaltyodemeleri_immutable ON \"PenaltyOdemeleri\";");

            migrationBuilder.DropTable(
                name: "PenaltyOdemeleri");

            migrationBuilder.DropTable(
                name: "PenaltySatirlari");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Penalties_TenantId_Id",
                table: "Penalties");

            migrationBuilder.DropIndex(
                name: "IX_Penalties_TenantId_MakbuzNo",
                table: "Penalties");

            migrationBuilder.DropIndex(
                name: "IX_AccountLedgerEntries_CezaOdeme_Idem",
                table: "AccountLedgerEntries");

            migrationBuilder.DropColumn(
                name: "CepTel",
                table: "Penalties");

            migrationBuilder.DropColumn(
                name: "IslemSube",
                table: "Penalties");

            migrationBuilder.DropColumn(
                name: "Kalan",
                table: "Penalties");

            migrationBuilder.DropColumn(
                name: "MakbuzNo",
                table: "Penalties");

            migrationBuilder.DropColumn(
                name: "OdenenTutar",
                table: "Penalties");

            migrationBuilder.DropColumn(
                name: "OdenmeTarihi",
                table: "Penalties");

            migrationBuilder.DropColumn(
                name: "Saat",
                table: "Penalties");

            migrationBuilder.DropColumn(
                name: "Yer",
                table: "Penalties");
        }
    }
}
