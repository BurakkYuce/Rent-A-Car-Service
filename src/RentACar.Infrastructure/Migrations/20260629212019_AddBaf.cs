using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBaf : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Baflar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    No = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PersonelId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    CikisTarihi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CikisKm = table.Column<int>(type: "integer", nullable: false),
                    CikisYakit = table.Column<int>(type: "integer", nullable: true),
                    DonusTarihi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DonusKm = table.Column<int>(type: "integer", nullable: true),
                    DonusYakit = table.Column<int>(type: "integer", nullable: true),
                    Sube = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Baflar", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Baflar_TenantId_No",
                table: "Baflar",
                columns: new[] { "TenantId", "No" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Baflar_TenantId_PersonelId",
                table: "Baflar",
                columns: new[] { "TenantId", "PersonelId" });

            // roadmap L5: RLS — tenant izolasyonu (ELLE). Full-CRUD (mali değişmez belge DEĞİL).
            migrationBuilder.Sql("ALTER TABLE \"Baflar\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"Baflar\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"Baflar\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"Baflar\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"Baflar\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Baflar");
        }
    }
}
