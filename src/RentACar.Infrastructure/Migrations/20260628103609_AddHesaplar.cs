using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHesaplar : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Hesaplar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ad = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Tur = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Doviz = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Aktif = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Hesaplar", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Hesaplar_TenantId_Kod",
                table: "Hesaplar",
                columns: new[] { "TenantId", "Kod" },
                unique: true);

            // RLS (tenant izolasyonu). Master kayıt (mali belge DEĞİL — sadece tanım) → değişmezlik
            // trigger'ı YOK; tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"Hesaplar\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"Hesaplar\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"Hesaplar\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"Hesaplar\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"Hesaplar\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Hesaplar");
        }
    }
}
