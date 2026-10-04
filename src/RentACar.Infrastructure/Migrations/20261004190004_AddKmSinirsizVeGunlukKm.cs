using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKmSinirsizVeGunlukKm : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "KmSinirsiz",
                table: "Reservations",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "KmLimitGunluk",
                table: "Rentals",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "KmSinirsiz",
                table: "Rentals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            // Kabul düzeltmesi #366 M2: girdi tarafında "0" artık "sınırsız" değil "boş (grup/tarife limiti)".
            // Göç öncesi KmLimit=0 kayıtlar dönüşte SINIRSIZ faturalanıyordu (ReturnMath: 0 = sınırsız) —
            // açık kira düzenlemesinde sessizce grup limiti kazanmasınlar diye anlamları açık bayrağa taşınır.
            // RLS TUZAĞI: racar_owner NOBYPASSRLS → FORCE altında düz UPDATE 0 satır görür; NO FORCE/FORCE arası.
            migrationBuilder.Sql("""
                ALTER TABLE "Rentals" NO FORCE ROW LEVEL SECURITY;
                UPDATE "Rentals" SET "KmSinirsiz" = true WHERE "KmLimit" = 0;
                ALTER TABLE "Rentals" FORCE ROW LEVEL SECURITY;
                ALTER TABLE "Reservations" NO FORCE ROW LEVEL SECURITY;
                UPDATE "Reservations" SET "KmSinirsiz" = true WHERE "KmLimit" = 0;
                ALTER TABLE "Reservations" FORCE ROW LEVEL SECURITY;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "KmSinirsiz",
                table: "Reservations");

            migrationBuilder.DropColumn(
                name: "KmLimitGunluk",
                table: "Rentals");

            migrationBuilder.DropColumn(
                name: "KmSinirsiz",
                table: "Rentals");
        }
    }
}
