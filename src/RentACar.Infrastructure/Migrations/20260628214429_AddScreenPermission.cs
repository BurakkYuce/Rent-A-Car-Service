using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddScreenPermission : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EkranYetkileri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    EkranKodu = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AllowedRolesCsv = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Aktif = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EkranYetkileri", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EkranYetkileri_TenantId_EkranKodu",
                table: "EkranYetkileri",
                columns: new[] { "TenantId", "EkranKodu" },
                unique: true);

            // RLS (tenant izolasyonu). Yetki-yapılandırma kaydı → değişmezlik trigger'ı YOK; tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"EkranYetkileri\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"EkranYetkileri\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"EkranYetkileri\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"EkranYetkileri\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"EkranYetkileri\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EkranYetkileri");
        }
    }
}
