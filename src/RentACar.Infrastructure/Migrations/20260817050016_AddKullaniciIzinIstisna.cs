using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKullaniciIzinIstisna : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KullaniciIzinIstisnalari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    Izin = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Ver = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    TanimlayanKullanici = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KullaniciIzinIstisnalari", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KullaniciIzinIstisnalari_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KullaniciIzinIstisnalari_TenantId_UserId_Izin",
                table: "KullaniciIzinIstisnalari",
                columns: new[] { "TenantId", "UserId", "Izin" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_KullaniciIzinIstisnalari_UserId",
                table: "KullaniciIzinIstisnalari",
                column: "UserId");

            // ---- RLS: Users tablosuyla AYNI komut-bazlı desen (FORCE değil, ENABLE) ----
            // SELECT, GUC boşken AÇIK: istisnalar LOGIN SIRASINDA (kimlik/tenant henüz yokken)
            // okunup claim'e yazılır — o yolda sorgu zaten (TenantId, UserId) ile dar okur.
            // GUC doluyken yalnız kendi tenant'ı görünür (raw SQL dahil çapraz-tenant okuma kapalı).
            // YAZMA (INSERT/UPDATE/DELETE) daima tenant'a kısıtlı — login yolunda yazma yok.
            migrationBuilder.Sql("ALTER TABLE \"KullaniciIzinIstisnalari\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql(
                "CREATE POLICY istisna_select ON \"KullaniciIzinIstisnalari\" FOR SELECT USING (" +
                "NULLIF(current_setting('app.tenant_id', true), '') IS NULL " +
                "OR \"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql(
                "CREATE POLICY istisna_insert ON \"KullaniciIzinIstisnalari\" FOR INSERT " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql(
                "CREATE POLICY istisna_update ON \"KullaniciIzinIstisnalari\" FOR UPDATE " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            // Users'tan FARK: DELETE var — istisna kaldırmak normal yönetim işlemi (kullanıcı
            // pasifleştirme gibi bir "soft" karşılığı yok, satırın kendisi yetki taşıyor).
            migrationBuilder.Sql(
                "CREATE POLICY istisna_delete ON \"KullaniciIzinIstisnalari\" FOR DELETE " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"KullaniciIzinIstisnalari\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KullaniciIzinIstisnalari");
        }
    }
}
