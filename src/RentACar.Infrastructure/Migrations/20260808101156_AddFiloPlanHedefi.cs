using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFiloPlanHedefi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FiloPlanHedefleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    AracGrupAdi = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Sipp = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: true),
                    Donem = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    HedefAdet = table.Column<int>(type: "integer", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiloPlanHedefleri", x => x.Id);
                });

            // NULLS NOT DISTINCT ŞART: üç boyut da nullable; PG varsayılanında NULL'lar farklı
            // sayıldığı için (grup, NULL, NULL) ikinci kez yazılabilir ve aynı hedef iki kez
            // tanımlanabilirdi. EF bunu üretemiyor → elle. (PG 15+; yerel 15.18, CI postgres:16.)
            migrationBuilder.Sql(
                "CREATE UNIQUE INDEX \"IX_FiloPlanHedefleri_Tenant_Grup_Sipp_Donem\" " +
                "ON \"FiloPlanHedefleri\" (\"TenantId\", \"AracGrupAdi\", \"Sipp\", \"Donem\") " +
                "NULLS NOT DISTINCT;");

            // RLS — EF ÜRETMEZ, elle (CLAUDE.md §5). Mali belge değil → tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"FiloPlanHedefleri\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"FiloPlanHedefleri\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"FiloPlanHedefleri\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"FiloPlanHedefleri\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"FiloPlanHedefleri\" TO racar_app;");

            // Hedef adet negatif olamaz (uygulama zaten reddediyor; DB de savunsun).
            migrationBuilder.Sql(
                "ALTER TABLE \"FiloPlanHedefleri\" ADD CONSTRAINT \"CK_FiloPlanHedefleri_HedefPozitif\" " +
                "CHECK (\"HedefAdet\" >= 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FiloPlanHedefleri");
        }
    }
}
