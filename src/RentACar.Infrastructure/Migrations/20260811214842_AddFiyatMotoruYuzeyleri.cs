using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <summary>
    /// FAZ-73 — fiyat motoru yüzey genişletmeleri (mevcut tenant-owned tablolara ekleme).
    ///
    /// <para><b>RLS bloğu YOK ve gerekmiyor:</b> yeni TABLO yok. <c>KiralamaKurallari</c> ve
    /// <c>DolulukFiyatKurallari</c> üzerinde ENABLE+FORCE ROW LEVEL SECURITY ve
    /// <c>tenant_isolation</c> politikası kendi kuruluş migration'larında var; politika SATIR
    /// düzeyinde çalıştığından yeni kolonları kendiliğinden kapsar, GRANT de tablo düzeyindedir.</para>
    ///
    /// <para><b>PARA NOTU (en kritik):</b> <c>KampanyaDurum</c> kolonunun kolon-varsayılanı 0
    /// (=Taslak) olduğundan MEVCUT kuralların hepsi taslağa düşerdi ve fiyat motoru
    /// (<c>ListActiveAsync</c>) HİÇBİR kuralı seçmezdi — her teklifin iskontosu sessizce sıfırlanırdı.
    /// Bu yüzden kolon eklendikten HEMEN SONRA backfill koşar: <c>Aktif=true → 2 (Aktif)</c>,
    /// <c>Aktif=false → 3 (Pasif)</c>. Böylece göç sonrası motorun gördüğü küme göç öncesiyle
    /// BİREBİR aynıdır.</para>
    ///
    /// <para><b>RLS TUZAĞI:</b> <c>racar_owner</c> NOBYPASSRLS'tir → FORCE altında düz UPDATE 0 satır
    /// görür. Backfill bu yüzden <c>NO FORCE</c> / <c>FORCE</c> arasına alınır (CLAUDE.md reçetesi).</para>
    ///
    /// <para><b>Değişmez çiti:</b> backfill sonrası <c>ck_kural_durum_senkron</c> CHECK'i
    /// <c>"Aktif" = ("KampanyaDurum" = 2)</c> ilişkisini DB'de zorlar — iki alanın ileride sessizce
    /// ayrışması yapısal olarak imkânsız (uygulama kemeri: RentalRuleService.Normalize).</para>
    ///
    /// <para><c>DolulukFiyatKurallari.SadeceKendiSubeleri</c> <c>defaultValue: false</c> alır: mevcut
    /// kurallar şube-agnostik kalır (doğru ifade) ve doluluk çarpanının davranışı değişmez.</para>
    /// </summary>
    public partial class AddFiyatMotoruYuzeyleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "KampanyaDurum",
                table: "KiralamaKurallari",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            // BACKFILL — bkz. sınıf notundaki PARA NOTU. Tek seferlik, idempotent (ikinci kez
            // koşsa aynı sonucu üretir); yalnız bu iki değeri yazar, başka kolona dokunmaz.
            migrationBuilder.Sql("""
                ALTER TABLE "KiralamaKurallari" NO FORCE ROW LEVEL SECURITY;
                UPDATE "KiralamaKurallari" SET "KampanyaDurum" = CASE WHEN "Aktif" THEN 2 ELSE 3 END;
                ALTER TABLE "KiralamaKurallari" FORCE ROW LEVEL SECURITY;
                """);

            // Senkron çiti (pantolon askısı): Aktif ile KampanyaDurum ayrışamaz.
            migrationBuilder.Sql("""
                ALTER TABLE "KiralamaKurallari" ADD CONSTRAINT ck_kural_durum_senkron
                CHECK ("Aktif" = ("KampanyaDurum" = 2));
                """);

            migrationBuilder.AddColumn<int>(
                name: "TarihTipi",
                table: "KiralamaKurallari",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<bool>(
                name: "SadeceKendiSubeleri",
                table: "DolulukFiyatKurallari",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Sube",
                table: "DolulukFiyatKurallari",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SubeId",
                table: "DolulukFiyatKurallari",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_DolulukFiyatKurallari_SubeId",
                table: "DolulukFiyatKurallari",
                column: "SubeId");

            migrationBuilder.CreateIndex(
                name: "IX_DolulukFiyatKurallari_TenantId_SubeId",
                table: "DolulukFiyatKurallari",
                columns: new[] { "TenantId", "SubeId" });

            migrationBuilder.AddForeignKey(
                name: "FK_DolulukFiyatKurallari_Branches_TenantId_SubeId",
                table: "DolulukFiyatKurallari",
                columns: new[] { "TenantId", "SubeId" },
                principalTable: "Branches",
                principalColumns: new[] { "TenantId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "ALTER TABLE \"KiralamaKurallari\" DROP CONSTRAINT IF EXISTS ck_kural_durum_senkron;");

            migrationBuilder.DropForeignKey(
                name: "FK_DolulukFiyatKurallari_Branches_TenantId_SubeId",
                table: "DolulukFiyatKurallari");

            migrationBuilder.DropIndex(
                name: "IX_DolulukFiyatKurallari_SubeId",
                table: "DolulukFiyatKurallari");

            migrationBuilder.DropIndex(
                name: "IX_DolulukFiyatKurallari_TenantId_SubeId",
                table: "DolulukFiyatKurallari");

            migrationBuilder.DropColumn(
                name: "KampanyaDurum",
                table: "KiralamaKurallari");

            migrationBuilder.DropColumn(
                name: "TarihTipi",
                table: "KiralamaKurallari");

            migrationBuilder.DropColumn(
                name: "SadeceKendiSubeleri",
                table: "DolulukFiyatKurallari");

            migrationBuilder.DropColumn(
                name: "Sube",
                table: "DolulukFiyatKurallari");

            migrationBuilder.DropColumn(
                name: "SubeId",
                table: "DolulukFiyatKurallari");
        }
    }
}
