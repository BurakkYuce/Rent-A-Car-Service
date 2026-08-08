using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDropTanimDerinlik : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DropTanimlari_TenantId_Lokasyon_Sube",
                table: "DropTanimlari");

            migrationBuilder.AddColumn<string>(
                name: "CikisLokasyon",
                table: "DropTanimlari",
                type: "character varying(150)",
                maxLength: 150,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Drop2",
                table: "DropTanimlari",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ManSuresi",
                table: "DropTanimlari",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MinGun",
                table: "DropTanimlari",
                type: "integer",
                nullable: true);

            // EF'in ürettiği CreateIndex(unique: true) NULLS NOT DISTINCT BASMAZ. Postgres
            // varsayılanında NULL'lar birbirinden farklı sayılır → (Lokasyon, Sube, NULL) ikinci
            // kez yazılabilir ve ESKİ benzersizlik garantisi SESSİZCE kaybolurdu. Bu yüzden indeks
            // elle kuruluyor. (PG 15+ gerekir; yerel 15.18, CI postgres:16 — ikisinde de doğrulandı.)
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_DropTanimlari_Tenant_Lokasyon_Sube_CikisLokasyon\" " +
                "ON \"DropTanimlari\" (\"TenantId\", \"Lokasyon\", \"Sube\", \"CikisLokasyon\") " +
                "NULLS NOT DISTINCT;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_DropTanimlari_Tenant_Lokasyon_Sube_CikisLokasyon",
                table: "DropTanimlari");

            migrationBuilder.DropColumn(
                name: "CikisLokasyon",
                table: "DropTanimlari");

            migrationBuilder.DropColumn(
                name: "Drop2",
                table: "DropTanimlari");

            migrationBuilder.DropColumn(
                name: "ManSuresi",
                table: "DropTanimlari");

            migrationBuilder.DropColumn(
                name: "MinGun",
                table: "DropTanimlari");

            migrationBuilder.CreateIndex(
                name: "IX_DropTanimlari_TenantId_Lokasyon_Sube",
                table: "DropTanimlari",
                columns: new[] { "TenantId", "Lokasyon", "Sube" },
                unique: true);
        }
    }
}
