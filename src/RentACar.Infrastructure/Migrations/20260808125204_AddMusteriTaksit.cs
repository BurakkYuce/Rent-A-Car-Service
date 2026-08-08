using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddMusteriTaksit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Vehicles_TenantId_Id",
                table: "Vehicles",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_Customers_TenantId_Id",
                table: "Customers",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "MusteriTaksitleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CariId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    VehicleSaleId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sira = table.Column<int>(type: "integer", nullable: false),
                    Vade = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TaksitTutari = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Kur = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    OdemeTarihi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MusteriTaksitleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MusteriTaksitleri_Customers_TenantId_CariId",
                        columns: x => new { x.TenantId, x.CariId },
                        principalTable: "Customers",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MusteriTaksitleri_Vehicles_TenantId_VehicleId",
                        columns: x => new { x.TenantId, x.VehicleId },
                        principalTable: "Vehicles",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_MusteriTaksitleri_TenantId_CariId",
                table: "MusteriTaksitleri",
                columns: new[] { "TenantId", "CariId" });

            migrationBuilder.CreateIndex(
                name: "IX_MusteriTaksitleri_TenantId_Vade",
                table: "MusteriTaksitleri",
                columns: new[] { "TenantId", "Vade" });

            migrationBuilder.CreateIndex(
                name: "IX_MusteriTaksitleri_TenantId_VehicleId",
                table: "MusteriTaksitleri",
                columns: new[] { "TenantId", "VehicleId" });

            // RLS — EF ÜRETMEZ, elle (CLAUDE.md §5). Takip kaydı, mali belge DEĞİL → tam CRUD grant
            // (immutability trigger yok: taksit satırı düzeltilebilir/silinebilir olmalı).
            migrationBuilder.Sql("ALTER TABLE \"MusteriTaksitleri\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"MusteriTaksitleri\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"MusteriTaksitleri\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"MusteriTaksitleri\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"MusteriTaksitleri\" TO racar_app;");

            // Uygulama zaten reddediyor; DB de kendi başına savunsun.
            migrationBuilder.Sql(
                "ALTER TABLE \"MusteriTaksitleri\" ADD CONSTRAINT \"CK_MusteriTaksitleri_TutarPozitif\" " +
                "CHECK (\"TaksitTutari\" > 0);");
            migrationBuilder.Sql(
                "ALTER TABLE \"MusteriTaksitleri\" ADD CONSTRAINT \"CK_MusteriTaksitleri_KurPozitif\" " +
                "CHECK (\"Kur\" > 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MusteriTaksitleri");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Vehicles_TenantId_Id",
                table: "Vehicles");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Customers_TenantId_Id",
                table: "Customers");
        }
    }
}
