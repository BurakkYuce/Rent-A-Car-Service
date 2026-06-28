using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Personeller",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ad = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Soyad = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    TcKimlikEnc = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    IseGiris = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    IseCikis = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    SurucuBelgeNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    MaasEnc = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Sube = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Aktif = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Personeller", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Personeller_TenantId_Kod",
                table: "Personeller",
                columns: new[] { "TenantId", "Kod" },
                unique: true);

            // RLS (tenant izolasyonu). Master (mali belge DEĞİL) → değişmezlik trigger'ı YOK; tam CRUD grant.
            // PII alanları zaten uygulama katmanında şifreli (ISecretProtector).
            migrationBuilder.Sql("ALTER TABLE \"Personeller\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"Personeller\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"Personeller\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"Personeller\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"Personeller\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Personeller");
        }
    }
}
