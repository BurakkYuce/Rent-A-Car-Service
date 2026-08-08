using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServisKaydiDerinlik : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BeyanTuru",
                table: "ServiceRecords",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CikisYakit",
                table: "ServiceRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "DegerKaybi",
                table: "ServiceRecords",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DonusYakit",
                table: "ServiceRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FaturaGenelToplam",
                table: "ServiceRecords",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FaturaKdv",
                table: "ServiceRecords",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "FaturaNo",
                table: "ServiceRecords",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "FaturaTarihi",
                table: "ServiceRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "FaturaTutar",
                table: "ServiceRecords",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HasarDosyaNo",
                table: "ServiceRecords",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "HesapNo",
                table: "ServiceRecords",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KarsiPlaka",
                table: "ServiceRecords",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KarsiTrafikSigortasi",
                table: "ServiceRecords",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KasaKodu",
                table: "ServiceRecords",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KazaSorumlusu",
                table: "ServiceRecords",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "KazaTarihi",
                table: "ServiceRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Odeme",
                table: "ServiceRecords",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OdemeDoviz",
                table: "ServiceRecords",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "OdemeKur",
                table: "ServiceRecords",
                type: "numeric(18,6)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OdemeTarihi",
                table: "ServiceRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "OdemeTuru",
                table: "ServiceRecords",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PlanBasTarihi",
                table: "ServiceRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "PlanBitTarihi",
                table: "ServiceRecords",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "BirimFiyat",
                table: "ServiceLines",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Indirim",
                table: "ServiceLines",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KdvOran",
                table: "ServiceLines",
                type: "numeric(5,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Miktar",
                table: "ServiceLines",
                type: "numeric(19,4)",
                nullable: true);

            // ================== FAZ-16 — el ile eklenen DB değişmezleri ==================
            // MIGRATION NOTU (defaultValue tuzağı): eklenen TÜM kolonlar NULLABLE'dır, dolayısıyla
            // hiçbir mevcut satıra yanlış anlam yüklenmez. Non-null + 0/false default olsaydı
            // "fatura tutarı 0 = bedava servis" ya da "ödeme 0 = ödendi" diye okunurdu (FAZ-15
            // dersi) → BACKFILL GEREKMEZ; bu yüzden "RLS altında düz UPDATE 0 satır görür"
            // tuzağına da hiç girilmiyor.
            // RLS: "ServiceRecords"/"ServiceLines" tablolarında ENABLE + FORCE ROW LEVEL SECURITY
            // ve tenant_isolation policy ZATEN var (AddServiceRecords); kolon eklemek policy'yi ve
            // grant'leri etkilemez → yeni RLS bloğu YOK.
            // ServisDurum.Rezerve (=4) yalnız yeni bir enum değeridir; kolon int olduğundan şema
            // değişmez, eski satırların 0-3 değerleri aynen korunur.

            // İkinci savunma katmanı (uygulama doğrulamasıyla simetrik). Fatura/ödeme TUTARLARINA
            // CHECK konmadı: bunlar bilgi alanı; ileride iade/alacak dekontu negatif gerektirebilir.
            // Uygulama bugün negatifi reddediyor, DB'yi kilitlemek gereksiz katılık olurdu.
            migrationBuilder.Sql("""
                ALTER TABLE "ServiceRecords"
                  ADD CONSTRAINT "CK_ServiceRecords_OdemeKurPozitif"
                      CHECK ("OdemeKur" IS NULL OR "OdemeKur" > 0),
                  ADD CONSTRAINT "CK_ServiceRecords_YakitOlcegi"
                      CHECK (("CikisYakit" IS NULL OR ("CikisYakit" >= 0 AND "CikisYakit" <= 12))
                         AND ("DonusYakit" IS NULL OR ("DonusYakit" >= 0 AND "DonusYakit" <= 12))),
                  ADD CONSTRAINT "CK_ServiceRecords_PlanSirasi"
                      CHECK ("PlanBasTarihi" IS NULL OR "PlanBitTarihi" IS NULL
                             OR "PlanBitTarihi" >= "PlanBasTarihi");
                """);

            // KdvOran bir ORANDIR (0..1) — "0,20 TL" niyetiyle yazılmasını DB de reddeder.
            migrationBuilder.Sql("""
                ALTER TABLE "ServiceLines"
                  ADD CONSTRAINT "CK_ServiceLines_KdvOrani"
                      CHECK ("KdvOran" IS NULL OR ("KdvOran" >= 0 AND "KdvOran" <= 1)),
                  ADD CONSTRAINT "CK_ServiceLines_MiktarNegatifDegil"
                      CHECK ("Miktar" IS NULL OR "Miktar" >= 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE "ServiceRecords"
                  DROP CONSTRAINT IF EXISTS "CK_ServiceRecords_OdemeKurPozitif",
                  DROP CONSTRAINT IF EXISTS "CK_ServiceRecords_YakitOlcegi",
                  DROP CONSTRAINT IF EXISTS "CK_ServiceRecords_PlanSirasi";
                ALTER TABLE "ServiceLines"
                  DROP CONSTRAINT IF EXISTS "CK_ServiceLines_KdvOrani",
                  DROP CONSTRAINT IF EXISTS "CK_ServiceLines_MiktarNegatifDegil";
                """);

            migrationBuilder.DropColumn(
                name: "BeyanTuru",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "CikisYakit",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "DegerKaybi",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "DonusYakit",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "FaturaGenelToplam",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "FaturaKdv",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "FaturaNo",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "FaturaTarihi",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "FaturaTutar",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "HasarDosyaNo",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "HesapNo",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "KarsiPlaka",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "KarsiTrafikSigortasi",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "KasaKodu",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "KazaSorumlusu",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "KazaTarihi",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "Odeme",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "OdemeDoviz",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "OdemeKur",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "OdemeTarihi",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "OdemeTuru",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "PlanBasTarihi",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "PlanBitTarihi",
                table: "ServiceRecords");

            migrationBuilder.DropColumn(
                name: "BirimFiyat",
                table: "ServiceLines");

            migrationBuilder.DropColumn(
                name: "Indirim",
                table: "ServiceLines");

            migrationBuilder.DropColumn(
                name: "KdvOran",
                table: "ServiceLines");

            migrationBuilder.DropColumn(
                name: "Miktar",
                table: "ServiceLines");
        }
    }
}
