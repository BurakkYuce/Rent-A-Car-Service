using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <summary>
    /// Müşteri bildirim altyapısı: tenant başına mesaj şablonu (tür + kanal) ve giden mesaj kaydı.
    ///
    /// <para><b>GidenMesajlar.Anahtar üzerindeki benzersiz index idempotency'yi ŞEMAYA koyar:</b>
    /// job iki kez koşsa da (çoklu instance, elle tetikleme) müşteri aynı mesajı iki kez almaz.
    /// Uygulama katmanının "önce sorgula sonra yaz" kontrolü yarışta yetersizdir.</para>
    ///
    /// <para><b>DegerlerJson</b> gövdenin YENİDEN üretilebilmesi içindir: şablon olay anında yoksa
    /// mesaj kuyrukta bekler ve şablon yazıldığında doğru içerikle gönderilir. Bu kolon olmasaydı
    /// kurulumun ilk gününde (henüz şablon yokken) gelen tüm mesajlar kalıcı ölürdü.</para>
    ///
    /// <para>İkisi de tenant-owned → ENABLE + FORCE ROW LEVEL SECURITY + tenant_isolation politikası
    /// ve racar_app'e tam CRUD grant (aşağıda ELLE eklendi; EF üretmez).</para>
    /// </summary>
    public partial class AddMesajSablonVeGidenMesaj : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GidenMesajlar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Anahtar = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Tur = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kanal = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Alici = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Konu = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Govde = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    DegerlerJson = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: true),
                    Durum = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Hata = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    DenemeSayisi = table.Column<int>(type: "integer", nullable: false),
                    KaynakTur = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    KaynakId = table.Column<Guid>(type: "uuid", nullable: true),
                    OlusturmaUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    GonderimUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GidenMesajlar", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MesajSablonlari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tur = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Kanal = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Konu = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    Govde = table.Column<string>(type: "character varying(8192)", maxLength: 8192, nullable: false),
                    Aktif = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MesajSablonlari", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GidenMesajlar_TenantId_Anahtar",
                table: "GidenMesajlar",
                columns: new[] { "TenantId", "Anahtar" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GidenMesajlar_TenantId_Durum_OlusturmaUtc",
                table: "GidenMesajlar",
                columns: new[] { "TenantId", "Durum", "OlusturmaUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_MesajSablonlari_TenantId_Tur_Kanal",
                table: "MesajSablonlari",
                columns: new[] { "TenantId", "Tur", "Kanal" },
                unique: true);
            // ---- RLS (tenant izolasyonu). EF ÜRETMEZ, elle eklenir (CLAUDE.md §5-4).
            // İkisi de mali belge DEĞİL → değişmezlik trigger'ı YOK; tam CRUD grant.
            // GidenMesajlar bir işlem kaydıdır ama DURUMU güncellenir (kuyrukta → gönderildi/başarısız)
            // ve şablon düzeltilince yeniden denenir; bu yüzden UPDATE serbest.
            foreach (var tablo in new[] { "MesajSablonlari", "GidenMesajlar" })
            {
                migrationBuilder.Sql($"ALTER TABLE \"{tablo}\" ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE \"{tablo}\" FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON \"{tablo}\";");
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON \"{tablo}\" " +
                    "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                    "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
                migrationBuilder.Sql($"GRANT SELECT, INSERT, UPDATE, DELETE ON \"{tablo}\" TO racar_app;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GidenMesajlar");

            migrationBuilder.DropTable(
                name: "MesajSablonlari");
        }
    }
}
