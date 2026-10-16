using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKabisBildirimleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "KabisEtkin",
                table: "Ayarlar",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "KabisKullanici",
                table: "Ayarlar",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KabisKurum",
                table: "Ayarlar",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "KabisSifreEnc",
                table: "Ayarlar",
                type: "character varying(1024)",
                maxLength: 1024,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "KabisTeslimiEngelle",
                table: "Ayarlar",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "KabisBildirimleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    KiraId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tur = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Durum = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    Kurum = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    OlayUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DenemeSayisi = table.Column<int>(type: "integer", nullable: false),
                    SonHata = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    DisReferans = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Elle = table.Column<bool>(type: "boolean", nullable: false),
                    Gerekce = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    IsaretleyenKullaniciId = table.Column<Guid>(type: "uuid", nullable: true),
                    OlusturmaUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SonDenemeUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SonrakiDenemeUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SahiplenmeBitisUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    BildirimUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KabisBildirimleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KabisBildirimleri_Rentals_KiraId",
                        column: x => x.KiraId,
                        principalTable: "Rentals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KabisBildirimleri_KiraId",
                table: "KabisBildirimleri",
                column: "KiraId");

            migrationBuilder.CreateIndex(
                name: "IX_KabisBildirimleri_TenantId_Durum_SonrakiDenemeUtc",
                table: "KabisBildirimleri",
                columns: new[] { "TenantId", "Durum", "SonrakiDenemeUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_KabisBildirimleri_TenantId_KiraId_Tur",
                table: "KabisBildirimleri",
                columns: new[] { "TenantId", "KiraId", "Tur" },
                unique: true);

            // ---- RLS (tenant izolasyonu). EF ÜRETMEZ, elle eklenir (CLAUDE.md §5-4).
            // Mali belge DEĞİL → değişmezlik trigger'ı YOK. Durum güncellenir (Bekliyor → Bildirildi/Hata/GerekliDegil),
            // satır kira silinirse (FK CASCADE) gider; tam CRUD grant. Ayarlar'a eklenen KABİS kolonları mevcut
            // politikanın altında (yeni tablo değil).
            migrationBuilder.Sql("ALTER TABLE \"KabisBildirimleri\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"KabisBildirimleri\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"KabisBildirimleri\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"KabisBildirimleri\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"KabisBildirimleri\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KabisBildirimleri");

            migrationBuilder.DropColumn(
                name: "KabisEtkin",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "KabisKullanici",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "KabisKurum",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "KabisSifreEnc",
                table: "Ayarlar");

            migrationBuilder.DropColumn(
                name: "KabisTeslimiEngelle",
                table: "Ayarlar");
        }
    }
}
