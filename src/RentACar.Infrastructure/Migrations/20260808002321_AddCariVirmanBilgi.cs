using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCariVirmanBilgi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CariVirmanBilgileri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    KaynakCariId = table.Column<Guid>(type: "uuid", nullable: false),
                    HedefCariId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Vade = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    MakbuzNo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Sube = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    IslemYapan = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CariVirmanBilgileri", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CariVirmanBilgileri_TenantId_HedefCariId",
                table: "CariVirmanBilgileri",
                columns: new[] { "TenantId", "HedefCariId" });

            migrationBuilder.CreateIndex(
                name: "IX_CariVirmanBilgileri_TenantId_KaynakCariId",
                table: "CariVirmanBilgileri",
                columns: new[] { "TenantId", "KaynakCariId" });

            migrationBuilder.CreateIndex(
                name: "IX_CariVirmanBilgileri_TenantId_Tarih",
                table: "CariVirmanBilgileri",
                columns: new[] { "TenantId", "Tarih" });

            // RLS (tenant izolasyonu) — EF üretmez, CLAUDE.md §5 gereği ELLE yazılır.
            // Künye tablosu PARA TAŞIMAZ (mali belge değil) → değişmezlik trigger'ı YOK, tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"CariVirmanBilgileri\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"CariVirmanBilgileri\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"CariVirmanBilgileri\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"CariVirmanBilgileri\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"CariVirmanBilgileri\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CariVirmanBilgileri");
        }
    }
}
