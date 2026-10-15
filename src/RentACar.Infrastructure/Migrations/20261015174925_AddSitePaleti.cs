using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <summary>
    /// Halka açık site palet seti (<c>Ayarlar.SitePaleti</c>, null = temanın varsayılan seti). Yalnız EKLEMELİ kolon:
    /// "Ayarlar" tablosunda RLS (ENABLE + FORCE + tenant_isolation) ve racar_app'in TABLO düzeyi CRUD yetkisi
    /// (AddTenantSettings) zaten var — yeni kolonu da kapsar, ek GRANT/policy gerekmez.
    /// </summary>
    public partial class AddSitePaleti : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SitePaleti",
                table: "Ayarlar",
                type: "character varying(30)",
                maxLength: 30,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SitePaleti",
                table: "Ayarlar");
        }
    }
}
