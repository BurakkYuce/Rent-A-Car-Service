using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBildirim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Bildirimler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tur = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    VadeTarihi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Mesaj = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Okundu = table.Column<bool>(type: "boolean", nullable: false),
                    OlusturmaTarihi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Bildirimler", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Bildirimler_TenantId_Okundu",
                table: "Bildirimler",
                columns: new[] { "TenantId", "Okundu" });

            migrationBuilder.CreateIndex(
                name: "IX_Bildirimler_TenantId_Tur_VehicleId_VadeTarihi",
                table: "Bildirimler",
                columns: new[] { "TenantId", "Tur", "VehicleId", "VadeTarihi" },
                unique: true);

            // ---- RLS (ELLE; EF üretmez): tenant izolasyonu — iki savunma katmanının DB tarafı ----
            migrationBuilder.Sql("ALTER TABLE \"Bildirimler\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"Bildirimler\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"Bildirimler\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"Bildirimler\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"Bildirimler\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Bildirimler");
        }
    }
}
