using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFaturaDonemi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FaturaDonemleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    RentalId = table.Column<Guid>(type: "uuid", nullable: false),
                    DonemSira = table.Column<int>(type: "integer", nullable: false),
                    DonemBas = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    DonemBit = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: true),
                    KesilenTutar = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FaturaDonemleri", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FaturaDonemleri_TenantId_RentalId_DonemSira",
                table: "FaturaDonemleri",
                columns: new[] { "TenantId", "RentalId", "DonemSira" },
                unique: true);

            // RLS (EF üretmez — ELLE, CLAUDE.md §5): tenant izolasyonu + FORCE; plan satırı güncellenir/
            // silinir (Planlandi yeniden üretimi) → tam CRUD grant. Kesildi koruması uygulama katmanında.
            migrationBuilder.Sql("ALTER TABLE \"FaturaDonemleri\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"FaturaDonemleri\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"FaturaDonemleri\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"FaturaDonemleri\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"FaturaDonemleri\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FaturaDonemleri");
        }
    }
}
