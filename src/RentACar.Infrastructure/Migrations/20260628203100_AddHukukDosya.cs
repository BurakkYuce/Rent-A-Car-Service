using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddHukukDosya : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "HukukDosyalari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    DosyaNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    CariId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tur = table.Column<int>(type: "integer", nullable: false),
                    Avukat = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Tutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    Aktif = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HukukDosyalari", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_HukukDosyalari_TenantId_DosyaNo",
                table: "HukukDosyalari",
                columns: new[] { "TenantId", "DosyaNo" },
                unique: true);

            // RLS (tenant izolasyonu). Master (mali belge DEĞİL) → değişmezlik trigger'ı YOK; tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"HukukDosyalari\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"HukukDosyalari\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"HukukDosyalari\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"HukukDosyalari\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"HukukDosyalari\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HukukDosyalari");
        }
    }
}
