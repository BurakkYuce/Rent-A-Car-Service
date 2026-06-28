using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCrmAnketSikayet : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Anketler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CariId = table.Column<Guid>(type: "uuid", nullable: true),
                    Puan = table.Column<int>(type: "integer", nullable: false),
                    Yorum = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Kaynak = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Anketler", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Sikayetler",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CariId = table.Column<Guid>(type: "uuid", nullable: true),
                    Konu = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Detay = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Cozum = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sikayetler", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Anketler_TenantId_Tarih",
                table: "Anketler",
                columns: new[] { "TenantId", "Tarih" });

            migrationBuilder.CreateIndex(
                name: "IX_Sikayetler_TenantId_Tarih",
                table: "Sikayetler",
                columns: new[] { "TenantId", "Tarih" });

            // RLS (tenant izolasyonu). CRM kaydı (mali belge DEĞİL) → trigger YOK; tam CRUD grant.
            foreach (var t in new[] { "Anketler", "Sikayetler" })
            {
                migrationBuilder.Sql($"ALTER TABLE \"{t}\" ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE \"{t}\" FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON \"{t}\";");
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON \"{t}\" " +
                    "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                    "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
                migrationBuilder.Sql($"GRANT SELECT, INSERT, UPDATE, DELETE ON \"{t}\" TO racar_app;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Anketler");

            migrationBuilder.DropTable(
                name: "Sikayetler");
        }
    }
}
