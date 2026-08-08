using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReservationSourceOranlar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<decimal>(
                name: "DropOrani",
                table: "RezervasyonKaynaklari",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "HizmetOrani",
                table: "RezervasyonKaynaklari",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KiraOrani",
                table: "RezervasyonKaynaklari",
                type: "numeric(5,2)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Tedarikci",
                table: "RezervasyonKaynaklari",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            // Oranlar YÜZDE: 0..100 dışı bir değer işaret/birim hatasıdır (ör. 0,125 katsayı
            // sanılıp girilirse %0,13 olur — ama negatif/100 üstü kesin hatadır). Uygulama zaten
            // reddediyor; bu DB tarafı ikinci savunma (elle eklendi — EF üretmez).
            // Mevcut satırların hepsi NULL olduğu için kısıt eklemek güvenli.
            migrationBuilder.Sql("""
                ALTER TABLE "RezervasyonKaynaklari"
                  ADD CONSTRAINT "CK_RezervasyonKaynaklari_OranAralik" CHECK (
                      ("KiraOrani"   IS NULL OR ("KiraOrani"   >= 0 AND "KiraOrani"   <= 100))
                  AND ("HizmetOrani" IS NULL OR ("HizmetOrani" >= 0 AND "HizmetOrani" <= 100))
                  AND ("DropOrani"   IS NULL OR ("DropOrani"   >= 0 AND "DropOrani"   <= 100)));
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """ALTER TABLE "RezervasyonKaynaklari" DROP CONSTRAINT IF EXISTS "CK_RezervasyonKaynaklari_OranAralik";""");

            migrationBuilder.DropColumn(
                name: "DropOrani",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "HizmetOrani",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "KiraOrani",
                table: "RezervasyonKaynaklari");

            migrationBuilder.DropColumn(
                name: "Tedarikci",
                table: "RezervasyonKaynaklari");
        }
    }
}
