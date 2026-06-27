using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddRateCards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "RateCards",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ad = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Grup = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    MinGun = table.Column<int>(type: "integer", nullable: false),
                    MaxGun = table.Column<int>(type: "integer", nullable: false),
                    GunlukUcret = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Doviz = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    GecerliBas = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GecerliBit = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Aktif = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RateCards", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_RateCards_TenantId_Grup",
                table: "RateCards",
                columns: new[] { "TenantId", "Grup" });

            migrationBuilder.CreateIndex(
                name: "IX_RateCards_TenantId_Kod",
                table: "RateCards",
                columns: new[] { "TenantId", "Kod" },
                unique: true);

            // RLS (tenant izolasyonu). Tarife master (mali belge DEĞİL) → değişmezlik trigger'ı
            // YOK; tam CRUD grant (yönetim ekranı düzenler/siler).
            migrationBuilder.Sql("ALTER TABLE \"RateCards\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"RateCards\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"RateCards\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"RateCards\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"RateCards\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RateCards");
        }
    }
}
