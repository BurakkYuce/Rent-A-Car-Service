using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDropTanim : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DropTanimlari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Lokasyon = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    Sube = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    KarsilamaSekli = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    CalismaSekli = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    OzelIletisim = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    Aktif = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DropTanimlari", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DropTanimlari_TenantId_Lokasyon_Sube",
                table: "DropTanimlari",
                columns: new[] { "TenantId", "Lokasyon", "Sube" },
                unique: true);

            // roadmap N2: RLS — tenant izolasyonu (ELLE). Full-CRUD basit master.
            migrationBuilder.Sql("ALTER TABLE \"DropTanimlari\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"DropTanimlari\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"DropTanimlari\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"DropTanimlari\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"DropTanimlari\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DropTanimlari");
        }
    }
}
