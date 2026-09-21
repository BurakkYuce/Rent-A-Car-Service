using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <summary>
    /// F3.5 — yeni arayüz tablo motorunun kişisel düzenleri (kullanıcı × tablo başına tek satır:
    /// sütun sırası/görünürlüğü/genişliği + sıralama, jsonb).
    ///
    /// <para>Tenant-owned → ENABLE + FORCE ROW LEVEL SECURITY + tenant_isolation politikası ve racar_app'e
    /// tam CRUD grant (aşağıda ELLE eklendi; EF üretmez). Mali belge DEĞİL → değişmezlik trigger'ı yok:
    /// düzen güncellenir (upsert) ve "varsayılana dön" satırı siler.</para>
    ///
    /// <para>Kullanıcı boyutu RLS'te değil servis katmanında (<c>TabloDuzeniService</c> kullanıcıyı
    /// oturumdan alır, uçta kullanıcı parametresi yok); <c>(TenantId, UserId, TabloKodu)</c> unique
    /// index eşzamanlı ilk yazımı tek satıra indirir.</para>
    /// </summary>
    public partial class AddTabloDuzenleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "TabloDuzenleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<Guid>(type: "uuid", nullable: false),
                    TabloKodu = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Duzen = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TabloDuzenleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TabloDuzenleri_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TabloDuzenleri_TenantId_UserId_TabloKodu",
                table: "TabloDuzenleri",
                columns: new[] { "TenantId", "UserId", "TabloKodu" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TabloDuzenleri_UserId",
                table: "TabloDuzenleri",
                column: "UserId");

            // ---- RLS (tenant izolasyonu). EF ÜRETMEZ, elle eklenir (CLAUDE.md §5-4).
            migrationBuilder.Sql("ALTER TABLE \"TabloDuzenleri\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"TabloDuzenleri\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"TabloDuzenleri\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"TabloDuzenleri\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"TabloDuzenleri\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TabloDuzenleri");
        }
    }
}
