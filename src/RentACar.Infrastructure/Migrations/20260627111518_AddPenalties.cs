using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPenalties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Penalties",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    No = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CezaTuru = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TebligTarihi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VadeTarihi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    CariId = table.Column<Guid>(type: "uuid", nullable: true),
                    RentalId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Sebep = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Penalties", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Penalties_TenantId_CariId",
                table: "Penalties",
                columns: new[] { "TenantId", "CariId" });

            migrationBuilder.CreateIndex(
                name: "IX_Penalties_TenantId_No",
                table: "Penalties",
                columns: new[] { "TenantId", "No" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Penalties_TenantId_VehicleId",
                table: "Penalties",
                columns: new[] { "TenantId", "VehicleId" });

            // RLS (tenant izolasyonu). Ceza BAŞLIĞI güncellenebilir (durum: Yansitildi/Odendi/
            // Iptal) → değişmezlik trigger'ı YOK; SELECT/INSERT/UPDATE grant (DELETE yok).
            // Yansıtma DEFTERİ (AccountLedgerEntries) zaten DB-immutable. FOR UPDATE satır
            // kilidi UPDATE yetkisi gerektirir → grant'ta var.
            migrationBuilder.Sql("ALTER TABLE \"Penalties\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"Penalties\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"Penalties\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"Penalties\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"Penalties\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Penalties");
        }
    }
}
