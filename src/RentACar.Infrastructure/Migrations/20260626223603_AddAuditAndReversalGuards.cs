using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAuditAndReversalGuards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_CashTransactions_TenantId_TersAlinanId",
                table: "CashTransactions",
                columns: new[] { "TenantId", "TersAlinanId" },
                unique: true,
                filter: "\"TersAlinanId\" IS NOT NULL");

            // DENETİM İZİ DEĞİŞMEZLİĞİ: AuditLog mali kanıttır — tenant içinde bile tahrif
            // edilemez. Trigger UPDATE/DELETE'i engeller; runtime rolünden de izin geri alınır.
            // (rc_prevent_mutation AddCashAndLedger'da tanımlı.)
            migrationBuilder.Sql(
                "CREATE TRIGGER auditlogs_immutable BEFORE UPDATE OR DELETE ON \"AuditLogs\" " +
                "FOR EACH ROW EXECUTE FUNCTION rc_prevent_mutation();");
            migrationBuilder.Sql("REVOKE UPDATE, DELETE ON \"AuditLogs\" FROM racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("GRANT UPDATE, DELETE ON \"AuditLogs\" TO racar_app;");
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS auditlogs_immutable ON \"AuditLogs\";");

            migrationBuilder.DropIndex(
                name: "IX_CashTransactions_TenantId_TersAlinanId",
                table: "CashTransactions");
        }
    }
}
