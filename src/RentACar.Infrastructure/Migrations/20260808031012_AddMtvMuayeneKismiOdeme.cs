using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMtvMuayeneKismiOdeme : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Aciklama",
                table: "MtvRecords",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Kalan",
                table: "MtvRecords",
                type: "numeric(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "Aciklama",
                table: "InspectionRecords",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "IslemKm",
                table: "InspectionRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Kalan",
                table: "InspectionRecords",
                type: "numeric(19,4)",
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_MtvRecords_TenantId_Id",
                table: "MtvRecords",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_InspectionRecords_TenantId_Id",
                table: "InspectionRecords",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "MtvOdemeleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    MtvId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sira = table.Column<int>(type: "integer", nullable: false),
                    Tutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    KalanSonrasi = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Hesap = table.Column<int>(type: "integer", nullable: false),
                    KasaKodu = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    HesapNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EvrakNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IslemYapan = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IslemAnahtari = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MtvOdemeleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MtvOdemeleri_MtvRecords_TenantId_MtvId",
                        columns: x => new { x.TenantId, x.MtvId },
                        principalTable: "MtvRecords",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MuayeneOdemeleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InspectionId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sira = table.Column<int>(type: "integer", nullable: false),
                    Tutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Ceza = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    KalanSonrasi = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Hesap = table.Column<int>(type: "integer", nullable: false),
                    KasaKodu = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    HesapNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EvrakNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    IslemYapan = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IslemAnahtari = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MuayeneOdemeleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MuayeneOdemeleri_InspectionRecords_TenantId_InspectionId",
                        columns: x => new { x.TenantId, x.InspectionId },
                        principalTable: "InspectionRecords",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MtvOdemeleri_TenantId_IslemAnahtari",
                table: "MtvOdemeleri",
                columns: new[] { "TenantId", "IslemAnahtari" },
                unique: true,
                filter: "\"IslemAnahtari\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_MtvOdemeleri_TenantId_MtvId_Sira",
                table: "MtvOdemeleri",
                columns: new[] { "TenantId", "MtvId", "Sira" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MuayeneOdemeleri_TenantId_InspectionId_Sira",
                table: "MuayeneOdemeleri",
                columns: new[] { "TenantId", "InspectionId", "Sira" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MuayeneOdemeleri_TenantId_IslemAnahtari",
                table: "MuayeneOdemeleri",
                columns: new[] { "TenantId", "IslemAnahtari" },
                unique: true,
                filter: "\"IslemAnahtari\" IS NOT NULL");

            // ---- RLS (EF ÜRETMEZ — CLAUDE.md §5) ----
            // Ödeme satırları MALİ BELGEDİR: racar_app'e yalnız SELECT + INSERT verilir.
            // UPDATE/DELETE yetkisi VERİLMEZ → uygulama bir ödemeyi ne değiştirebilir ne silebilir
            // (immutability trigger'a gerek kalmadan yapısal koruma).
            foreach (var t in new[] { "MtvOdemeleri", "MuayeneOdemeleri" })
            {
                migrationBuilder.Sql($"ALTER TABLE \"{t}\" ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE \"{t}\" FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON \"{t}\";");
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON \"{t}\" " +
                    "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                    "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
                migrationBuilder.Sql($"GRANT SELECT, INSERT ON \"{t}\" TO racar_app;");
                // Adversarial M4: grant TEK BAŞINA yetmez — racar_owner (migrator/ops betikleri)
                // hâlâ UPDATE/DELETE edebilirdi ve o an Kalan ile Σ ödeme sessizce ayrışırdı.
                // Diğer mali tablolarla AYNI koruma: DB seviyesinde değişmezlik trigger'ı.
                migrationBuilder.Sql(
                    $"CREATE TRIGGER {t.ToLowerInvariant()}_immutable BEFORE UPDATE OR DELETE ON \"{t}\" " +
                    "FOR EACH ROW EXECUTE FUNCTION rc_prevent_mutation();");
            }

            // Ödeme tutarı pozitif olmalı (uygulama zaten reddediyor; DB de kendi başına savunsun).
            migrationBuilder.Sql(
                "ALTER TABLE \"MtvOdemeleri\" ADD CONSTRAINT \"CK_MtvOdemeleri_TutarPozitif\" CHECK (\"Tutar\" > 0);");
            migrationBuilder.Sql(
                "ALTER TABLE \"MuayeneOdemeleri\" ADD CONSTRAINT \"CK_MuayeneOdemeleri_TutarPozitif\" CHECK (\"Tutar\" > 0);");

            // ---- BACKFILL ----
            // TUZAK: bu tablolarda FORCE ROW LEVEL SECURITY açık ve racar_owner'da BYPASSRLS YOK
            // (ampirik doğrulandı: rolbypassrls=false). Migration'da düz UPDATE yazsaydık policy
            // app.tenant_id'yi boş görüp SIFIR satır günceller ve backfill SESSİZCE hiçbir şey
            // yapmazdı. Bu yüzden NO FORCE / FORCE parantezine alınıyor (aynı transaction).
            migrationBuilder.Sql("ALTER TABLE \"MtvRecords\" NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "UPDATE \"MtvRecords\" SET \"Kalan\" = CASE WHEN \"Odendi\" THEN 0 ELSE \"Tutar\" END;");
            migrationBuilder.Sql("ALTER TABLE \"MtvRecords\" FORCE ROW LEVEL SECURITY;");

            // Muayene: ödenmemişte kalan = Ucret (ceza ödeme anında doğar, açılışta borç değil);
            // ödenmişte 0.
            migrationBuilder.Sql("ALTER TABLE \"InspectionRecords\" NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "UPDATE \"InspectionRecords\" SET \"Kalan\" = CASE WHEN \"Odendi\" THEN 0 ELSE \"Ucret\" END;");
            migrationBuilder.Sql("ALTER TABLE \"InspectionRecords\" FORCE ROW LEVEL SECURITY;");

            // AYNI TUZAĞIN ESKİ KURBANI: 20260707121111_BackfillAracDurumMusait ve
            // 20260708220430_ConvertStoktaDurumToMusait düz UPDATE yazdıkları için 0 satır
            // güncellemişti; dev DB'de hâlâ Durum=0 (kaldırılmış "Stokta") araç bulundu. Aynı
            // parantezle tekrarlanıyor — idempotent (WHERE Durum=0).
            migrationBuilder.Sql("ALTER TABLE \"Vehicles\" NO FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("UPDATE \"Vehicles\" SET \"Durum\" = 1 WHERE \"Durum\" = 0;");
            migrationBuilder.Sql("ALTER TABLE \"Vehicles\" FORCE ROW LEVEL SECURITY;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MtvOdemeleri");

            migrationBuilder.DropTable(
                name: "MuayeneOdemeleri");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_MtvRecords_TenantId_Id",
                table: "MtvRecords");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_InspectionRecords_TenantId_Id",
                table: "InspectionRecords");

            migrationBuilder.DropColumn(
                name: "Aciklama",
                table: "MtvRecords");

            migrationBuilder.DropColumn(
                name: "Kalan",
                table: "MtvRecords");

            migrationBuilder.DropColumn(
                name: "Aciklama",
                table: "InspectionRecords");

            migrationBuilder.DropColumn(
                name: "IslemKm",
                table: "InspectionRecords");

            migrationBuilder.DropColumn(
                name: "Kalan",
                table: "InspectionRecords");
        }
    }
}
