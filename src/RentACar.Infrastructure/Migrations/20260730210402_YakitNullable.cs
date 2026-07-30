using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class YakitNullable : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Yakit",
                table: "Vehicles",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            // ---- PR-21: MEVCUT DEĞERLER "GİRİLMEDİ"YE ÇEKİLİR (kullanıcı onaylı) ----
            // Kolon bugüne kadar NOT NULL + varsayılan `Benzin` idi; yani hiçbir satırdaki "Benzin"
            // bilinçli bir giriş OLDUĞU KANITLANAMAZ — form yakıt seçilmeden kaydedildiğinde de
            // Benzin yazıyordu. Canlıda 26 aracın 26'sı "Benzin" görünüyordu ve bu bilgi halka açık
            // siteye, ilan başlığına ve sözleşmeye basılıyordu.
            // Yanlış bilgi yayınlamak, bilgi yayınlamamaktan kötüdür → hepsi NULL'a çekilir,
            // personel araç ekranından doğrusunu girer.
            // NOT: Down() bunu geri getiremez (hangi satırın gerçekten Benzin olduğu bilinmiyor);
            // geri alım kolonu NOT NULL yapıp Benzin'e düşürür — yani ileri yön tek yönlüdür.
            // DİKKAT — DÜZ `UPDATE` BURADA SESSİZCE 0 SATIR ETKİLER.
            // `Vehicles` FORCE ROW LEVEL SECURITY taşıyor ve migration'ı çalıştıran `racar_owner`
            // rolünün BYPASSRLS yetkisi YOK (bilinçli). `app.tenant_id` GUC'u set edilmeden
            // policy hiçbir satırı eşleştirmez ve UPDATE hata VERMEDEN hiçbir şey yapmaz —
            // ilk yazımda tam olarak bu oldu, kolon nullable oldu ama 26 satır Benzin kaldı.
            // Çözüm PiiBackfill'in deseni: tenant döngüsü + İŞLEM-YEREL set_config.
            migrationBuilder.Sql("""
                DO $$
                DECLARE t uuid;
                BEGIN
                    FOR t IN SELECT "Id" FROM "Tenants" LOOP
                        PERFORM set_config('app.tenant_id', t::text, true);
                        UPDATE "Vehicles" SET "Yakit" = NULL;
                    END LOOP;
                END $$;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<int>(
                name: "Yakit",
                table: "Vehicles",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);
        }
    }
}
