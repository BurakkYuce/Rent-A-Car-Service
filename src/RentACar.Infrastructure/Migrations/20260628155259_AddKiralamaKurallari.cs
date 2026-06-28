using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKiralamaKurallari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "KiralamaKurallari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ad = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Kanal = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Sube = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    AracGrupKod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    MinGun = table.Column<int>(type: "integer", nullable: true),
                    MaxGun = table.Column<int>(type: "integer", nullable: true),
                    Iskonto = table.Column<decimal>(type: "numeric(9,4)", nullable: true),
                    SonraOdeOran = table.Column<decimal>(type: "numeric(9,4)", nullable: true),
                    HediyeGun = table.Column<int>(type: "integer", nullable: true),
                    KampanyaMi = table.Column<bool>(type: "boolean", nullable: false),
                    KampanyaKodu = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    GecerlilikBas = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GecerlilikBit = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SartMetni = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    Aktif = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KiralamaKurallari", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KiralamaKurallari_TenantId_Kod",
                table: "KiralamaKurallari",
                columns: new[] { "TenantId", "Kod" },
                unique: true);

            // RLS (tenant izolasyonu). Master/kural-tanım (mali belge DEĞİL) → değişmezlik trigger'ı
            // YOK; tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"KiralamaKurallari\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"KiralamaKurallari\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"KiralamaKurallari\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"KiralamaKurallari\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"KiralamaKurallari\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KiralamaKurallari");
        }
    }
}
