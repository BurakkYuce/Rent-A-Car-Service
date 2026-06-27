using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddVehicleSalesAndDamageFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DamageFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    No = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    RentalId = table.Column<Guid>(type: "uuid", nullable: true),
                    CariId = table.Column<Guid>(type: "uuid", nullable: true),
                    AcilisTarihi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    TahminiTutar = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    OnayNotu = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DamageFiles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "VehicleSales",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    No = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    AliciCariId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NoterNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SatisNet = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    KdvOrani = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    KdvTutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    GenelToplam = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Kur = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VehicleSales", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DamageFiles_TenantId_Durum",
                table: "DamageFiles",
                columns: new[] { "TenantId", "Durum" });

            migrationBuilder.CreateIndex(
                name: "IX_DamageFiles_TenantId_No",
                table: "DamageFiles",
                columns: new[] { "TenantId", "No" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DamageFiles_TenantId_VehicleId",
                table: "DamageFiles",
                columns: new[] { "TenantId", "VehicleId" });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleSales_TenantId_AliciCariId",
                table: "VehicleSales",
                columns: new[] { "TenantId", "AliciCariId" });

            migrationBuilder.CreateIndex(
                name: "IX_VehicleSales_TenantId_No",
                table: "VehicleSales",
                columns: new[] { "TenantId", "No" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VehicleSales_TenantId_VehicleId",
                table: "VehicleSales",
                columns: new[] { "TenantId", "VehicleId" },
                unique: true,
                filter: "\"Durum\" = 0");

            // VehicleSales: MALİ BELGE → RLS + DEĞİŞMEZLİK (rc_prevent_mutation AddCashAndLedger'da).
            migrationBuilder.Sql("ALTER TABLE \"VehicleSales\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"VehicleSales\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"VehicleSales\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"VehicleSales\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON \"VehicleSales\" TO racar_app;");
            migrationBuilder.Sql(
                "CREATE TRIGGER vehiclesales_immutable BEFORE UPDATE OR DELETE ON \"VehicleSales\" " +
                "FOR EACH ROW EXECUTE FUNCTION rc_prevent_mutation();");

            // DamageFiles: ONAY AKIŞI kaydı (mali belge DEĞİL) → RLS + tam CRUD grant, trigger YOK.
            migrationBuilder.Sql("ALTER TABLE \"DamageFiles\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"DamageFiles\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"DamageFiles\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"DamageFiles\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"DamageFiles\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS vehiclesales_immutable ON \"VehicleSales\";");
            migrationBuilder.DropTable(
                name: "DamageFiles");

            migrationBuilder.DropTable(
                name: "VehicleSales");
        }
    }
}
