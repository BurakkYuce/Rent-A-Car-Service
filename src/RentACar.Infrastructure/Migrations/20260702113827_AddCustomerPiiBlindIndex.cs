using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomerPiiBlindIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Customers_TenantId_TcKimlik",
                table: "Customers");

            migrationBuilder.AddColumn<string>(
                name: "EhliyetNoEnc",
                table: "Customers",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PasaportNoEnc",
                table: "Customers",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TcKimlikEnc",
                table: "Customers",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "TcKimlikHash",
                table: "Customers",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Customers_TenantId_TcKimlikHash",
                table: "Customers",
                columns: new[] { "TenantId", "TcKimlikHash" },
                unique: true,
                filter: "\"TcKimlikHash\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
            // GERİ ALINAMAZ (adversarial M2): PiiBackfill düz metni sildikten sonra *Enc
            // kolonlarını düşürmek TC/ehliyet/pasaportun TEK kopyasını imha eder; üstelik
            // tamamen NULL kolona plaintext unique index geri kurulurdu.
            => throw new NotSupportedException(
                "AddCustomerPiiBlindIndex geri alınamaz: Enc kolonları PII'nın tek kopyasıdır (KVKK/F2).");
    }
}
