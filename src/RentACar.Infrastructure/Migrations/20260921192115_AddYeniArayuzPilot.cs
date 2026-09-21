using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <summary>
    /// Angular geçişi F1.2 — yeni arayüz pilot bayrağı (<c>Ayarlar.YeniArayuzPilot</c>, varsayılan false).
    /// <c>/api/ui/v1</c>'in oturum dışındaki uçları yalnız pilot firmaya açıktır (sunucu filtresi).
    ///
    /// <para>RLS: "Ayarlar" zaten ENABLE + FORCE ROW LEVEL SECURITY, tenant_isolation politikası ve
    /// tablo düzeyinde tam CRUD grant taşıyor (bkz. AddTenantSettings). Tek additive kolon mevcut
    /// tabloya eklendiği için ek politika/grant GEREKMEZ (AddFaturaSeriKodu ile aynı durum).</para>
    /// </summary>
    public partial class AddYeniArayuzPilot : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "YeniArayuzPilot",
                table: "Ayarlar",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "YeniArayuzPilot",
                table: "Ayarlar");
        }
    }
}
