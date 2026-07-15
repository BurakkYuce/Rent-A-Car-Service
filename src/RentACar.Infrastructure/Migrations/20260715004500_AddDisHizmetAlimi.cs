using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDisHizmetAlimi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DisHizmetAlimlari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    No = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    RentalId = table.Column<Guid>(type: "uuid", nullable: false),
                    FaturaKesilecekCariId = table.Column<Guid>(type: "uuid", nullable: false),
                    BakiyeliCariId = table.Column<Guid>(type: "uuid", nullable: true),
                    AlinanHizmet = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    HizmetAlinanFirma = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    HizmetBedeli = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    TedarikciKomisyonOran = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    BayiKomisyonOran = table.Column<decimal>(type: "numeric(9,4)", nullable: true),
                    VerilecekKomisyonTutar = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    KomisyonFaturaNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    BayiFaturaNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    KdvMuaf = table.Column<bool>(type: "boolean", nullable: false),
                    IndirimTuru = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Kur = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    IslemAnahtari = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DisHizmetAlimlari", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DisHizmetAlimlari_TenantId_IslemAnahtari",
                table: "DisHizmetAlimlari",
                columns: new[] { "TenantId", "IslemAnahtari" },
                unique: true,
                filter: "\"IslemAnahtari\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_DisHizmetAlimlari_TenantId_No",
                table: "DisHizmetAlimlari",
                columns: new[] { "TenantId", "No" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DisHizmetAlimlari_TenantId_RentalId",
                table: "DisHizmetAlimlari",
                columns: new[] { "TenantId", "RentalId" });

            // RLS (EF üretmez — ELLE, CLAUDE.md §5): tenant izolasyonu + FORCE. Mali iz: DELETE yok
            // (düzeltme ters kayıtla), UPDATE yalnız Durum=Iptal geçişi için gerekli (uygulama katmanı
            // FOR UPDATE çitiyle korur) → GRANT SELECT, INSERT, UPDATE.
            migrationBuilder.Sql("ALTER TABLE \"DisHizmetAlimlari\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"DisHizmetAlimlari\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"DisHizmetAlimlari\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"DisHizmetAlimlari\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE ON \"DisHizmetAlimlari\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DisHizmetAlimlari");
        }
    }
}
