using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleKmLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VehicleKmLoglari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Km = table.Column<int>(type: "integer", nullable: false),
                    Kaynak = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleKmLoglari", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleKmLoglari_TenantId_VehicleId_Tarih",
                table: "VehicleKmLoglari",
                columns: new[] { "TenantId", "VehicleId", "Tarih" });

            // RLS (EF üretmez — ELLE, CLAUDE.md §5): tenant izolasyonu + FORCE. SALT-EKLEME tablo:
            // racar_app'e yalnız SELECT+INSERT — km serisi geriye dönük oynanamaz (UPDATE/DELETE yok).
            migrationBuilder.Sql("ALTER TABLE \"VehicleKmLoglari\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"VehicleKmLoglari\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"VehicleKmLoglari\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"VehicleKmLoglari\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON \"VehicleKmLoglari\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VehicleKmLoglari");
        }
    }
}
