using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBlogPost : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "BlogYazilari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Baslik = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Slug = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Ozet = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Icerik = table.Column<string>(type: "character varying(20000)", maxLength: 20000, nullable: false),
                    KapakBytes = table.Column<byte[]>(type: "bytea", nullable: true),
                    KapakThumbBytes = table.Column<byte[]>(type: "bytea", nullable: true),
                    KapakContentType = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    YayinTarihi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BlogYazilari", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BlogYazilari_TenantId_Durum_YayinTarihi",
                table: "BlogYazilari",
                columns: new[] { "TenantId", "Durum", "YayinTarihi" });

            migrationBuilder.CreateIndex(
                name: "IX_BlogYazilari_TenantId_Slug",
                table: "BlogYazilari",
                columns: new[] { "TenantId", "Slug" },
                unique: true);

            // ---- RLS (EF ÜRETMEZ — elle; CLAUDE.md §5 reçetesi). Mali belge DEĞİL → tam CRUD grant,
            // immutability trigger YOK (blog yazısı düzenlenebilir/silinebilir içerik). ----
            migrationBuilder.Sql("ALTER TABLE \"BlogYazilari\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"BlogYazilari\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"BlogYazilari\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"BlogYazilari\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"BlogYazilari\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BlogYazilari");
        }
    }
}
