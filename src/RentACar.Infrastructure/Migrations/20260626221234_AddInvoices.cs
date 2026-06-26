using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddInvoices : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Invoices",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    No = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    CariId = table.Column<Guid>(type: "uuid", nullable: false),
                    RentalId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NetTutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    KdvTutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    GenelToplam = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Kur = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    EFaturaEttn = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EFaturaGonderildi = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Invoices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InvoiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    InvoiceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Miktar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    BirimNetFiyat = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    KdvOrani = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    SatirNet = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    SatirKdv = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    SatirToplam = table.Column<decimal>(type: "numeric(19,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InvoiceLines_Invoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "Invoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_InvoiceId",
                table: "InvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_InvoiceLines_TenantId_InvoiceId",
                table: "InvoiceLines",
                columns: new[] { "TenantId", "InvoiceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_CariId",
                table: "Invoices",
                columns: new[] { "TenantId", "CariId" });

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_TenantId_No",
                table: "Invoices",
                columns: new[] { "TenantId", "No" },
                unique: true);

            // RLS + DEĞİŞMEZLİK: kesilmiş fatura ve satırları tenant-izole ve immutable
            // (rc_prevent_mutation AddCashAndLedger'da tanımlı). Düzeltme = ters/iptal.
            foreach (var table in new[] { "Invoices", "InvoiceLines" })
            {
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON \"{table}\";");
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON \"{table}\" " +
                    "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                    "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
                migrationBuilder.Sql($"GRANT SELECT, INSERT, UPDATE, DELETE ON \"{table}\" TO racar_app;");
                migrationBuilder.Sql(
                    $"CREATE TRIGGER {table.ToLowerInvariant()}_immutable BEFORE UPDATE OR DELETE ON \"{table}\" " +
                    "FOR EACH ROW EXECUTE FUNCTION rc_prevent_mutation();");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS invoices_immutable ON \"Invoices\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS invoicelines_immutable ON \"InvoiceLines\";");

            migrationBuilder.DropTable(
                name: "InvoiceLines");

            migrationBuilder.DropTable(
                name: "Invoices");
        }
    }
}
