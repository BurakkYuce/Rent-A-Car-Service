using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddJobCalismaLoglari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "JobCalismaLoglari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    JobAdi = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    BaslangicUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BitisUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Basarili = table.Column<bool>(type: "boolean", nullable: false),
                    SonucSayisi = table.Column<int>(type: "integer", nullable: true),
                    Detay = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JobCalismaLoglari", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_JobCalismaLoglari_TenantId_BaslangicUtc",
                table: "JobCalismaLoglari",
                columns: new[] { "TenantId", "BaslangicUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_JobCalismaLoglari_TenantId_JobAdi_BaslangicUtc",
                table: "JobCalismaLoglari",
                columns: new[] { "TenantId", "JobAdi", "BaslangicUtc" });

            // RLS (tenant izolasyonu) — EF üretmez, CLAUDE.md §5 gereği ELLE yazılır.
            // Sistem günlüğü (mali belge DEĞİL) → değişmezlik trigger'ı YOK, ama pratikte
            // append-only: uygulama yalnız INSERT eder. Temizlik/arşiv için DELETE açık bırakıldı.
            migrationBuilder.Sql("ALTER TABLE \"JobCalismaLoglari\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"JobCalismaLoglari\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"JobCalismaLoglari\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"JobCalismaLoglari\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"JobCalismaLoglari\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "JobCalismaLoglari");
        }
    }
}
