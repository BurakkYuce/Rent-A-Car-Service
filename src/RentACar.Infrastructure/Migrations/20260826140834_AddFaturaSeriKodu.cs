using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <summary>
    /// GİB fatura numarası için 3 karakterlik SERİ kodu.
    ///
    /// <para>Mevzuat: e-Fatura/e-Arşiv fatura numarası 16 hanedir — seri(3) + yıl(4) + sıra(9);
    /// sıra her yıl 1'den başlar ve her seri kendi içinde boşluksuz ilerler. Bu yüzden fatura,
    /// diğer belgelerin günlük 13 haneli deseninden AYRI biçimlenir. Fatura numarası kesildikten
    /// sonra değiştirilemez (rc_prevent_mutation), dolayısıyla doğru format baştan kullanılmalıdır;
    /// e-Fatura entegrasyonu sonradan açıldığında geriye dönük numaralandırma YAPILAMAZ.</para>
    ///
    /// <para>RLS: "Ayarlar" zaten ENABLE + FORCE ROW LEVEL SECURITY, tenant_isolation politikası ve
    /// tablo düzeyinde tam CRUD grant taşıyor (bkz. AddTenantSettings). Tek additive kolon mevcut
    /// tabloya eklendiği için ek politika/grant GEREKMEZ (AddSmtpGonderen ile aynı durum).</para>
    /// </summary>
    public partial class AddFaturaSeriKodu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "FaturaSeriKodu",
                table: "Ayarlar",
                type: "character varying(3)",
                maxLength: 3,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "FaturaSeriKodu",
                table: "Ayarlar");
        }
    }
}
