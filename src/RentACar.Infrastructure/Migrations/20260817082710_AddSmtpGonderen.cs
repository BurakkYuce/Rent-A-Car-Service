using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <summary>
    /// Bildirim omurgası: giden e-postanın "Kimden" adresi ve görünen adı.
    ///
    /// <para>Ayrı alan olmasının nedeni: SMTP kimlik doğrulama kullanıcı adı çoğu sağlayıcıda e-posta
    /// DEĞİLDİR ve alan adı doğrulaması (SPF/DKIM) gönderen adrese bakar — ikisini aynı alandan
    /// okumak, doğrulanmamış gönderenle spam'e düşen mesajlar üretirdi.</para>
    ///
    /// <para>RLS: "Ayarlar" zaten ENABLE + FORCE ROW LEVEL SECURITY, tenant_isolation politikası ve
    /// tablo düzeyinde tam CRUD grant taşıyor (bkz. AddTenantSettings). İki additive kolon mevcut
    /// tabloya eklendiği için ek politika/grant GEREKMEZ (AddWhatsAppBildirim ile aynı durum).</para>
    /// </summary>
    public partial class AddSmtpGonderen : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "SmtpGonderenAd",
                table: "Ayarlar",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SmtpGonderenAdres",
                table: "Ayarlar",
                type: "character varying(256)",
                maxLength: 256,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SmtpGonderenAd",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "SmtpGonderenAdres",
                table: "Ayarlar");
        }
    }
}
