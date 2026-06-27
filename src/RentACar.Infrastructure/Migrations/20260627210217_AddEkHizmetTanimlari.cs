using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddEkHizmetTanimlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "EkHizmetTanimlari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kod = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ad = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    BirimUcret = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    KdvOrani = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    Aktif = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EkHizmetTanimlari", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EkHizmetTanimlari_TenantId_Kod",
                table: "EkHizmetTanimlari",
                columns: new[] { "TenantId", "Kod" },
                unique: true);

            // RLS (tenant izolasyonu). Master kayıt (mali belge DEĞİL) → değişmezlik trigger'ı
            // YOK; tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"EkHizmetTanimlari\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"EkHizmetTanimlari\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"EkHizmetTanimlari\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"EkHizmetTanimlari\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"EkHizmetTanimlari\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EkHizmetTanimlari");
        }
    }
}
