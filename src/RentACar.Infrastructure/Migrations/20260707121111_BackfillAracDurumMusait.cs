using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class BackfillAracDurumMusait : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Mevcut araçlar default "Stokta"da (0) takılıydı; kira yaşam döngüsü artık Durum'u sürüyor
            // (teslim→Kirada, dönüş→Musait) ve yeni araç default'u Musait. Kirasız araçların "boşta/Musait"
            // görünmesi için eski Stokta kayıtlarını Musait'e (1) taşı. Owner-migrasyon RLS bypass → tüm tenant.
            // (Kirada/Serviste/Pasif/Satildi/Satıldı olanlara DOKUNMA — yalnız Stokta=0 → Musait=1.)
            migrationBuilder.Sql("UPDATE \"Vehicles\" SET \"Durum\" = 1 WHERE \"Durum\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Geri alma: Musait→Stokta ayırt edilemez (veri kaybı) → no-op.
        }
    }
}
