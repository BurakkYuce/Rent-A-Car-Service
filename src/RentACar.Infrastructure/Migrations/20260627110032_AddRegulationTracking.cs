using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRegulationTracking : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "InspectionRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    MuayeneTarihi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Bitis = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Ucret = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InspectionRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InsurancePolicies",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tip = table.Column<int>(type: "integer", nullable: false),
                    PoliceNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Baslangic = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Bitis = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Firma = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Acenta = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Prim = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InsurancePolicies", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MtvRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Donem = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Tutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Vade = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Odendi = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MtvRecords", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionRecords_TenantId_Bitis",
                table: "InspectionRecords",
                columns: new[] { "TenantId", "Bitis" });

            migrationBuilder.CreateIndex(
                name: "IX_InspectionRecords_TenantId_VehicleId",
                table: "InspectionRecords",
                columns: new[] { "TenantId", "VehicleId" });

            migrationBuilder.CreateIndex(
                name: "IX_InsurancePolicies_TenantId_Bitis",
                table: "InsurancePolicies",
                columns: new[] { "TenantId", "Bitis" });

            migrationBuilder.CreateIndex(
                name: "IX_InsurancePolicies_TenantId_VehicleId",
                table: "InsurancePolicies",
                columns: new[] { "TenantId", "VehicleId" });

            migrationBuilder.CreateIndex(
                name: "IX_MtvRecords_TenantId_Vade",
                table: "MtvRecords",
                columns: new[] { "TenantId", "Vade" });

            migrationBuilder.CreateIndex(
                name: "IX_MtvRecords_TenantId_VehicleId",
                table: "MtvRecords",
                columns: new[] { "TenantId", "VehicleId" });

            // RLS (tenant izolasyonu). Bunlar GÜNCELLENEBİLİR kayıtlar (mali belge değil) →
            // değişmezlik trigger'ı YOK; tam CRUD grant.
            foreach (var table in new[] { "InsurancePolicies", "MtvRecords", "InspectionRecords" })
            {
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON \"{table}\";");
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON \"{table}\" " +
                    "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                    "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
                migrationBuilder.Sql($"GRANT SELECT, INSERT, UPDATE, DELETE ON \"{table}\" TO racar_app;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "InspectionRecords");

            migrationBuilder.DropTable(
                name: "InsurancePolicies");

            migrationBuilder.DropTable(
                name: "MtvRecords");
        }
    }
}
