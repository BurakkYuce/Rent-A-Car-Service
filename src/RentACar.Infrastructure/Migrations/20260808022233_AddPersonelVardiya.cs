using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPersonelVardiya : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_Personeller_TenantId_Id",
                table: "Personeller",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "PersonelVardiyalari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    PersonelId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tarih = table.Column<DateOnly>(type: "date", nullable: false),
                    BaslangicSaat = table.Column<TimeOnly>(type: "time", nullable: false),
                    BitisSaat = table.Column<TimeOnly>(type: "time", nullable: false),
                    Sube = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    SubeId = table.Column<Guid>(type: "uuid", nullable: true),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PersonelVardiyalari", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PersonelVardiyalari_Branches_TenantId_SubeId",
                        columns: x => new { x.TenantId, x.SubeId },
                        principalTable: "Branches",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PersonelVardiyalari_Personeller_TenantId_PersonelId",
                        columns: x => new { x.TenantId, x.PersonelId },
                        principalTable: "Personeller",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PersonelVardiyalari_SubeId",
                table: "PersonelVardiyalari",
                column: "SubeId");

            migrationBuilder.CreateIndex(
                name: "IX_PersonelVardiyalari_TenantId_PersonelId_Tarih",
                table: "PersonelVardiyalari",
                columns: new[] { "TenantId", "PersonelId", "Tarih" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonelVardiyalari_TenantId_SubeId",
                table: "PersonelVardiyalari",
                columns: new[] { "TenantId", "SubeId" });

            migrationBuilder.CreateIndex(
                name: "IX_PersonelVardiyalari_TenantId_Tarih",
                table: "PersonelVardiyalari",
                columns: new[] { "TenantId", "Tarih" });

            // RLS — EF ÜRETMEZ, elle eklenir (CLAUDE.md §5). Vardiya mali belge değil → tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"PersonelVardiyalari\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"PersonelVardiyalari\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"PersonelVardiyalari\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"PersonelVardiyalari\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"PersonelVardiyalari\" TO racar_app;");

            // Sıfır uzunluklu vardiya UYGULAMADA reddediliyor; DB de kendi başına savunsun
            // (doğrudan SQL / ileride başka yazma yolu). Gece vardiyası (bitiş < başlangıç) SERBEST.
            migrationBuilder.Sql(
                "ALTER TABLE \"PersonelVardiyalari\" ADD CONSTRAINT \"CK_PersonelVardiyalari_SifirSure\" " +
                "CHECK (\"BaslangicSaat\" <> \"BitisSaat\");");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PersonelVardiyalari");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_Personeller_TenantId_Id",
                table: "Personeller");
        }
    }
}
