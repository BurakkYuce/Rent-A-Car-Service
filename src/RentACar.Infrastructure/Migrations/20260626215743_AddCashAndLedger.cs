using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCashAndLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "CariId",
                table: "AccountLedgerEntries",
                newName: "SourceId");

            migrationBuilder.AddColumn<Guid>(
                name: "AccountRef",
                table: "AccountLedgerEntries",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AccountType",
                table: "AccountLedgerEntries",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "SourceType",
                table: "AccountLedgerEntries",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "CashTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    No = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Tip = table.Column<int>(type: "integer", nullable: false),
                    CariId = table.Column<Guid>(type: "uuid", nullable: false),
                    RentalId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    KarsiHesap = table.Column<int>(type: "integer", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    TersKayitMi = table.Column<bool>(type: "boolean", nullable: false),
                    TersAlinanId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Amount_Value = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Amount_Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Amount_Rate = table.Column<decimal>(type: "numeric(19,6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CashTransactions", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AccountLedgerEntries_TenantId_AccountType_AccountRef",
                table: "AccountLedgerEntries",
                columns: new[] { "TenantId", "AccountType", "AccountRef" });

            migrationBuilder.CreateIndex(
                name: "IX_AccountLedgerEntries_TenantId_SourceType_SourceId",
                table: "AccountLedgerEntries",
                columns: new[] { "TenantId", "SourceType", "SourceId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_TenantId_CariId",
                table: "CashTransactions",
                columns: new[] { "TenantId", "CariId" });

            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_TenantId_No",
                table: "CashTransactions",
                columns: new[] { "TenantId", "No" },
                unique: true);

            // RLS (CashTransactions) — AccountLedgerEntries RLS'i InitialCreate'te.
            migrationBuilder.Sql("ALTER TABLE \"CashTransactions\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"CashTransactions\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"CashTransactions\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"CashTransactions\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"CashTransactions\" TO racar_app;");

            // ---------------------------------------------------------------
            // DEĞİŞMEZLİK (mali denetim): işlenmiş defter kaydı ve nakit belgesi
            // UPDATE/DELETE'e DB seviyesinde kapalı. Düzeltme = ters kayıt (yeni satır).
            // (DDL etkilenmez; yalnız satır DML'i engellenir.)
            // ---------------------------------------------------------------
            migrationBuilder.Sql(
                "CREATE OR REPLACE FUNCTION rc_prevent_mutation() RETURNS trigger AS $$ " +
                "BEGIN RAISE EXCEPTION 'Bu kayıt değişmezdir (UPDATE/DELETE yasak): %', TG_TABLE_NAME; END; " +
                "$$ LANGUAGE plpgsql;");
            migrationBuilder.Sql(
                "CREATE TRIGGER ledger_immutable BEFORE UPDATE OR DELETE ON \"AccountLedgerEntries\" " +
                "FOR EACH ROW EXECUTE FUNCTION rc_prevent_mutation();");
            migrationBuilder.Sql(
                "CREATE TRIGGER cash_immutable BEFORE UPDATE OR DELETE ON \"CashTransactions\" " +
                "FOR EACH ROW EXECUTE FUNCTION rc_prevent_mutation();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS ledger_immutable ON \"AccountLedgerEntries\";");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS cash_immutable ON \"CashTransactions\";");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS rc_prevent_mutation();");

            migrationBuilder.DropTable(
                name: "CashTransactions");

            migrationBuilder.DropIndex(
                name: "IX_AccountLedgerEntries_TenantId_AccountType_AccountRef",
                table: "AccountLedgerEntries");

            migrationBuilder.DropIndex(
                name: "IX_AccountLedgerEntries_TenantId_SourceType_SourceId",
                table: "AccountLedgerEntries");

            migrationBuilder.DropColumn(
                name: "AccountRef",
                table: "AccountLedgerEntries");

            migrationBuilder.DropColumn(
                name: "AccountType",
                table: "AccountLedgerEntries");

            migrationBuilder.DropColumn(
                name: "SourceType",
                table: "AccountLedgerEntries");

            migrationBuilder.RenameColumn(
                name: "SourceId",
                table: "AccountLedgerEntries",
                newName: "CariId");
        }
    }
}
