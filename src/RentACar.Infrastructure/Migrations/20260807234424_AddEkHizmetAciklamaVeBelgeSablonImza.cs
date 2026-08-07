using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEkHizmetAciklamaVeBelgeSablonImza : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Aciklama",
                table: "EkHizmetTanimlari",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxGun",
                table: "EkHizmetTanimlari",
                type: "integer",
                nullable: true);

            // defaultValue: TRUE — EF varsayılan olarak false üretir (C# property initializer'ını
            // görmez). false olsaydı MEVCUT tüm şablonlarda imza alanı sessizce KAPANIRDI; oysa bu
            // faz davranışı değiştirmemeli, yalnız kapatma seçeneği eklemeli.
            migrationBuilder.AddColumn<bool>(
                name: "ImzaAlaniGoster",
                table: "BelgeSablonlari",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            // DB varsayılanı yalnız GERİYE DÖNÜK doldurma içindi; kalıcı bırakılırsa model
            // (HasDefaultValue yok) ile şema birbirinden ayrışır. Yeni satırların değerini
            // uygulama yazar (entity initializer = true).
            migrationBuilder.Sql("ALTER TABLE \"BelgeSablonlari\" ALTER COLUMN \"ImzaAlaniGoster\" DROP DEFAULT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Aciklama",
                table: "EkHizmetTanimlari");

            migrationBuilder.DropColumn(
                name: "MaxGun",
                table: "EkHizmetTanimlari");

            migrationBuilder.DropColumn(
                name: "ImzaAlaniGoster",
                table: "BelgeSablonlari");
        }
    }
}
