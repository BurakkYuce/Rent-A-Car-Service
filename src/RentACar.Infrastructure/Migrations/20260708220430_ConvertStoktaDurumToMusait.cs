using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class ConvertStoktaDurumToMusait : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Operasyonel VehicleStatus.Stokta (=0) kaldırıldı → mevcut 0 satırları Musait (=1) yap.
            // (Önceki BackfillAracDurumMusait'ten SONRA web/API default'u Stokta olduğundan yeni 0'lar oluşmuş
            //  olabilir; bu idempotent temizlik + default artık Musait olduğundan bir daha 0 üretilmez.)
            migrationBuilder.Sql("UPDATE \"Vehicles\" SET \"Durum\" = 1 WHERE \"Durum\" = 0;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {

        }
    }
}
