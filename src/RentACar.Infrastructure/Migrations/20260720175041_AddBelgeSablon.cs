using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBelgeSablon : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BelgeSablonId",
                table: "Rentals",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "BelgeSablonlari",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    BelgeTuru = table.Column<int>(type: "integer", nullable: false),
                    Ad = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    VarsayilanMi = table.Column<bool>(type: "boolean", nullable: false),
                    Aktif = table.Column<bool>(type: "boolean", nullable: false),
                    BelgeBasligi = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    HukukiMetinSol = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    HukukiMetinSag = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    EkKosullarVarsayilan = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                    AltBilgi = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BelgeSablonlari", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BelgeSablonlari_TenantId_BelgeTuru_Ad",
                table: "BelgeSablonlari",
                columns: new[] { "TenantId", "BelgeTuru", "Ad" },
                unique: true);

            // RLS (EF üretmez — ELLE, CLAUDE.md §5): tenant izolasyonu + FORCE; master (mali değil,
            // defter postalamaz) → immutability trigger YOK, tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"BelgeSablonlari\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"BelgeSablonlari\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"BelgeSablonlari\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"BelgeSablonlari\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"BelgeSablonlari\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "BelgeSablonlari");

            migrationBuilder.DropColumn(
                name: "BelgeSablonId",
                table: "Rentals");
        }
    }
}
