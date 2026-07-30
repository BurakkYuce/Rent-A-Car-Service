using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTalepDongusu : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "AtananAd",
                table: "SiteTalepleri",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "AtananKullaniciId",
                table: "SiteTalepleri",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddUniqueConstraint(
                name: "AK_SiteTalepleri_TenantId_Id",
                table: "SiteTalepleri",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "TalepNotlari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    TalepId = table.Column<Guid>(type: "uuid", nullable: false),
                    Metin = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                    Kullanici = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    ZamanUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TalepNotlari", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TalepNotlari_SiteTalepleri_TenantId_TalepId",
                        columns: x => new { x.TenantId, x.TalepId },
                        principalTable: "SiteTalepleri",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TalepNotlari_TenantId_TalepId_ZamanUtc",
                table: "TalepNotlari",
                columns: new[] { "TenantId", "TalepId", "ZamanUtc" });

            // ---- PR-17: TalepNotlari tenant-owned → RLS bloğu ELLE (CLAUDE.md §5) ----
            // Takip notu müşteri adı/telefonu bağlamı taşır; başka firmanın lead geçmişi görünmemeli.
            // Mali belge DEĞİL → immutability trigger'ı yok, ama NOT SİLİNMEZ (servis silme metodu
            // sunmuyor); DELETE grant'ı yalnız talep cascade'i için gerekli.
            migrationBuilder.Sql("ALTER TABLE \"TalepNotlari\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"TalepNotlari\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"TalepNotlari\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"TalepNotlari\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"TalepNotlari\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "TalepNotlari");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_SiteTalepleri_TenantId_Id",
                table: "SiteTalepleri");

            migrationBuilder.DropColumn(
                name: "AtananAd",
                table: "SiteTalepleri");

            migrationBuilder.DropColumn(
                name: "AtananKullaniciId",
                table: "SiteTalepleri");
        }
    }
}
