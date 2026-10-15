using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <summary>
    /// e-Belge (e-Fatura / e-Arşiv) kimliksiz altyapısı:
    /// <list type="bullet">
    /// <item><c>EBelgeler</c> — fatura başına tek gönderim kaydı (durum, ETTN, deneme/kilit izi). Fatura tablosu
    /// değişmez kaldığı için durum ayrı tabloda. İdempotency şemada: <c>(TenantId, InvoiceId)</c> ve
    /// <c>(TenantId, Ettn)</c> benzersiz. Tutar YOK — XML her gönderimde değişmez faturadan üretilir; XML'in kendisi
    /// (alıcı TCKN'si içerebilir) saklanmaz, yalnız SHA-256 özeti.</item>
    /// <item><c>Ayarlar</c>'a entegratör seçimi, GB/PK etiketleri, test/canlı ortam, senaryo ve firma il/ilçe
    /// (UBL-TR adresinde zorunlu). Şifre/API anahtarı mevcut şifreli <c>EFaturaSifreEnc</c> kolonunda.</item>
    /// </list>
    /// Mevcut faturalara e-belge kaydı AÇILMAZ (geçmişe dönük gönderim kullanıcı kararıdır: "Gönderime al").
    /// </summary>
    public partial class AddEBelgeler : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EFaturaCanliOrtam",
                table: "Ayarlar",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "EFaturaEntegrator",
                table: "Ayarlar",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EFaturaGondericiEtiket",
                table: "Ayarlar",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EFaturaPostaKutusuEtiket",
                table: "Ayarlar",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EFaturaSenaryo",
                table: "Ayarlar",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirmaIl",
                table: "Ayarlar",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FirmaIlce",
                table: "Ayarlar",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "EBelgeler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ettn = table.Column<Guid>(type: "uuid", nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    Tur = table.Column<int>(type: "integer", nullable: true),
                    Profil = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    FaturaTipi = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    DenemeSayisi = table.Column<int>(type: "integer", nullable: false),
                    SonDenemeUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SonrakiDenemeUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    KilitBitisUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GonderimUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    EntegratorRef = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EntegratorDurum = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SonMesaj = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    XmlOzet = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EBelgeler", x => x.Id);
                    table.ForeignKey(
                        name: "FK_EBelgeler_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EBelgeler_InvoiceId",
                table: "EBelgeler",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_EBelgeler_TenantId_Durum_SonrakiDenemeUtc",
                table: "EBelgeler",
                columns: new[] { "TenantId", "Durum", "SonrakiDenemeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_EBelgeler_TenantId_Ettn",
                table: "EBelgeler",
                columns: new[] { "TenantId", "Ettn" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_EBelgeler_TenantId_InvoiceId",
                table: "EBelgeler",
                columns: new[] { "TenantId", "InvoiceId" },
                unique: true);

            // ---- RLS (tenant izolasyonu). EF ÜRETMEZ, elle eklenir (CLAUDE.md §5-4).
            // e-Belge kaydı mali belge DEĞİL (tutar taşımaz; faturanın kendisi değişmez kalır) ama DURUMU güncellenir
            // (kuyruk → gönderildi / hata → yeniden kuyruk) → UPDATE serbest. DELETE verilmez: gönderim izi silinemez.
            migrationBuilder.Sql("ALTER TABLE \"EBelgeler\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"EBelgeler\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"EBelgeler\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"EBelgeler\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"EBelgeler\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EBelgeler");

            migrationBuilder.DropColumn(
                name: "EFaturaCanliOrtam",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "EFaturaEntegrator",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "EFaturaGondericiEtiket",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "EFaturaPostaKutusuEtiket",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "EFaturaSenaryo",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "FirmaIl",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "FirmaIlce",
                table: "Ayarlar");
        }
    }
}
