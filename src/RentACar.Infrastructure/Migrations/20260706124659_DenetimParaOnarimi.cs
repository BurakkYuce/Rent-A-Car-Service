using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class DenetimParaOnarimi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Denetim M2 (RETRO-K1) VERİ ONARIMI: eski ExtendAsync planlı uzatmayı hem Tutar'a hem
            // UzatmaBedeli'ne yazıyordu → BaseGross (Tutar + UzatmaBedeli) dönüş-öncesi faturada ÇİFT sayardı.
            // Kod düzeltildi (uzatma yalnız Tutar'a); mevcut AKTİF (Kirada=0) satırlarda çift kalan
            // UzatmaGun/UzatmaBedeli sıfırlanır. Ayrım güvenli: geç-dönüş bedeli yalnız Tamamlandı'da yazılır →
            // Kirada + UzatmaBedeli>0 kombinasyonu YALNIZ eski-kod uzatmasından gelebilir.
            // GenelToplam/Bakiye zaten doğruydu (uzatma tek kez eklenmişti) → dokunulmaz.
            // Migration owner (BYPASSRLS) ile koşar → tüm tenant'lar onarılır.
            migrationBuilder.Sql(
                "UPDATE \"Rentals\" SET \"UzatmaGun\" = 0, \"UzatmaBedeli\" = 0 " +
                "WHERE \"Durum\" = 0 AND \"UzatmaBedeli\" > 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
