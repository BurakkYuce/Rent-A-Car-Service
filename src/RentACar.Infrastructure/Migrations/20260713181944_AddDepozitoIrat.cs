using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDepozitoIrat : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DepozitoIratlar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    CariId = table.Column<Guid>(type: "uuid", nullable: false),
                    RentalId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Kur = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DepozitoIratlar", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DepozitoIratlar_TenantId_CariId",
                table: "DepozitoIratlar",
                columns: new[] { "TenantId", "CariId" });

            migrationBuilder.CreateIndex(
                name: "IX_DepozitoIratlar_TenantId_RentalId",
                table: "DepozitoIratlar",
                columns: new[] { "TenantId", "RentalId" });

            // RLS + DEĞİŞMEZLİK (CLAUDE.md §5): irat mali izdir — Expenses deseni birebir.
            migrationBuilder.Sql("ALTER TABLE \"DepozitoIratlar\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"DepozitoIratlar\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"DepozitoIratlar\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"DepozitoIratlar\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON \"DepozitoIratlar\" TO racar_app;");
            migrationBuilder.Sql(
                "CREATE TRIGGER depozito_iratlar_immutable BEFORE UPDATE OR DELETE ON \"DepozitoIratlar\" " +
                "FOR EACH ROW EXECUTE FUNCTION rc_prevent_mutation();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS depozito_iratlar_immutable ON \"DepozitoIratlar\";");
            migrationBuilder.DropTable(
                name: "DepozitoIratlar");
        }
    }
}
