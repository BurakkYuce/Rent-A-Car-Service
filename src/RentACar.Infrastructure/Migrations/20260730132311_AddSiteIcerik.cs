using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSiteIcerik : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SayfaIcerikler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Baslik = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Govde = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    MetaAciklama = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Sira = table.Column<int>(type: "integer", nullable: false),
                    Yayinda = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SayfaIcerikler", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SssKayitlari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Soru = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    Cevap = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    Sira = table.Column<int>(type: "integer", nullable: false),
                    Yayinda = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SssKayitlari", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SayfaIcerikler_TenantId_Slug",
                table: "SayfaIcerikler",
                columns: new[] { "TenantId", "Slug" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SayfaIcerikler_TenantId_Yayinda_Sira",
                table: "SayfaIcerikler",
                columns: new[] { "TenantId", "Yayinda", "Sira" });

            migrationBuilder.CreateIndex(
                name: "IX_SssKayitlari_TenantId_Yayinda_Sira",
                table: "SssKayitlari",
                columns: new[] { "TenantId", "Yayinda", "Sira" });

            // ---- PR-16: TENANT-OWNED tablolar → RLS bloğu ELLE (EF üretmez, CLAUDE.md §5) ----
            // İçerik firmaya özeldir: bir tenant'ın "Hakkımızda" metni başka bir firmanın sitesinde
            // görünmemeli. EF query filter + Postgres RLS iki katman birlikte açık kalır.
            foreach (var tablo in new[] { "SayfaIcerikler", "SssKayitlari" })
            {
                migrationBuilder.Sql($"ALTER TABLE \"{tablo}\" ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE \"{tablo}\" FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON \"{tablo}\";");
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON \"{tablo}\" " +
                    "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                    "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
                // Mali belge DEĞİL → immutability trigger'ı UYGULANMAZ; tam CRUD (personel metni düzenler/siler).
                migrationBuilder.Sql($"GRANT SELECT, INSERT, UPDATE, DELETE ON \"{tablo}\" TO racar_app;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SayfaIcerikler");

            migrationBuilder.DropTable(
                name: "SssKayitlari");
        }
    }
}
