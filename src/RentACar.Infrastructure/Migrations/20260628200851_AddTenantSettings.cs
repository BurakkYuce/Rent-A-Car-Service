using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddTenantSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Ayarlar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmaUnvan = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    FirmaVergiDairesi = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    FirmaVergiNo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    FirmaAdres = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    FirmaTel = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    FirmaEmail = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EFaturaKullanici = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    EFaturaSifreEnc = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    SmsBaslik = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    SmsApiKeyEnc = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    PosMerchantId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    PosApiKeyEnc = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Ayarlar", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Ayarlar_TenantId",
                table: "Ayarlar",
                column: "TenantId",
                unique: true);

            // RLS (tenant izolasyonu). Ayarlar mali belge DEĞİL → değişmezlik trigger'ı YOK; tam CRUD grant.
            // Sır alanları zaten uygulama katmanında şifreli (ISecretProtector) saklanır.
            migrationBuilder.Sql("ALTER TABLE \"Ayarlar\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"Ayarlar\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"Ayarlar\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"Ayarlar\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"Ayarlar\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Ayarlar");
        }
    }
}
