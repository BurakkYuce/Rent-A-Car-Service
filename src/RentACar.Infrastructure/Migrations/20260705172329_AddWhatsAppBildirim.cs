using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddWhatsAppBildirim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "WhatsAppGunlukOzet",
                table: "Ayarlar",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "WhatsAppNumarasi",
                table: "Ayarlar",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "WhatsAppGonderimler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Gun = table.Column<DateOnly>(type: "date", nullable: false),
                    Tur = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Alici = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ozet = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Basarili = table.Column<bool>(type: "boolean", nullable: false),
                    HataMesaji = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    OlusturmaTarihi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WhatsAppGonderimler", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WhatsAppGonderimler_TenantId_Gun_Tur",
                table: "WhatsAppGonderimler",
                columns: new[] { "TenantId", "Gun", "Tur" },
                unique: true);

            // ---- WhatsAppGonderimler: TENANT-OWNED RLS + FORCE + policy. Mali belge DEĞİL → immutability trigger YOK
            // (update serbest: başarısız→başarılı). Ayarlar 2 additive kolonu mevcut RLS tablosunda → ek grant yok. ----
            migrationBuilder.Sql("ALTER TABLE \"WhatsAppGonderimler\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"WhatsAppGonderimler\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"WhatsAppGonderimler\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"WhatsAppGonderimler\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"WhatsAppGonderimler\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "WhatsAppGonderimler");

            migrationBuilder.DropColumn(
                name: "WhatsAppGunlukOzet",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "WhatsAppNumarasi",
                table: "Ayarlar");
        }
    }
}
