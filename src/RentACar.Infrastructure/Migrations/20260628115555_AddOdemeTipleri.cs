using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddOdemeTipleri : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OdemeTipleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ad = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Aktif = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OdemeTipleri", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OdemeTipleri_TenantId_Kod",
                table: "OdemeTipleri",
                columns: new[] { "TenantId", "Kod" },
                unique: true);

            // RLS (tenant izolasyonu). Master kayıt (mali belge DEĞİL) → değişmezlik trigger'ı
            // YOK; tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"OdemeTipleri\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"OdemeTipleri\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"OdemeTipleri\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"OdemeTipleri\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"OdemeTipleri\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OdemeTipleri");
        }
    }
}
