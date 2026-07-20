using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddYetkiGrup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "YetkiGruplari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ad = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    KalemlerJson = table.Column<string>(type: "character varying(8000)", maxLength: 8000, nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_YetkiGruplari", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_YetkiGruplari_TenantId_Ad",
                table: "YetkiGruplari",
                columns: new[] { "TenantId", "Ad" },
                unique: true);

            // RLS — tenant izolasyonu (CLAUDE.md §5; EF üretmez → ELLE). Tenant-owned master, defter postalamaz
            // → immutability trigger YOK, tam CRUD grant. tenant_isolation policy set_config('app.tenant_id') okur.
            migrationBuilder.Sql("ALTER TABLE \"YetkiGruplari\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"YetkiGruplari\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"YetkiGruplari\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"YetkiGruplari\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"YetkiGruplari\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "YetkiGruplari");
        }
    }
}
