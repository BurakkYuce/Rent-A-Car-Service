using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddKapatmaTahsis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddUniqueConstraint(
                name: "AK_CashTransactions_TenantId_Id",
                table: "CashTransactions",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.AddUniqueConstraint(
                name: "AK_AccountLedgerEntries_TenantId_Id",
                table: "AccountLedgerEntries",
                columns: new[] { "TenantId", "Id" });

            migrationBuilder.CreateTable(
                name: "KapatmaTahsisleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    LedgerEntryId = table.Column<Guid>(type: "uuid", nullable: false),
                    CashTransactionId = table.Column<Guid>(type: "uuid", nullable: false),
                    CariId = table.Column<Guid>(type: "uuid", nullable: false),
                    KapatilanBaz = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_KapatmaTahsisleri", x => x.Id);
                    table.ForeignKey(
                        name: "FK_KapatmaTahsisleri_AccountLedgerEntries_TenantId_LedgerEntry~",
                        columns: x => new { x.TenantId, x.LedgerEntryId },
                        principalTable: "AccountLedgerEntries",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_KapatmaTahsisleri_CashTransactions_TenantId_CashTransaction~",
                        columns: x => new { x.TenantId, x.CashTransactionId },
                        principalTable: "CashTransactions",
                        principalColumns: new[] { "TenantId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_KapatmaTahsisleri_TenantId_CariId",
                table: "KapatmaTahsisleri",
                columns: new[] { "TenantId", "CariId" });

            migrationBuilder.CreateIndex(
                name: "IX_KapatmaTahsisleri_TenantId_CashTransactionId",
                table: "KapatmaTahsisleri",
                columns: new[] { "TenantId", "CashTransactionId" });

            migrationBuilder.CreateIndex(
                name: "IX_KapatmaTahsisleri_TenantId_LedgerEntryId",
                table: "KapatmaTahsisleri",
                columns: new[] { "TenantId", "LedgerEntryId" });

            // RLS bloğu ELLE eklendi (EF üretmez) — CLAUDE.md §5 adım 4.
            // Tahsis MALİ BELGE DEĞİLDİR (deftere postlamaz, tutarı bakiyeye girmez) → immutability
            // trigger'ı YOK, tam CRUD grant verilir. Yanlış tahsis geri alınabilmeli; asıl mali
            // kayıt olan tahsilat zaten ters kayıtla düzeltiliyor.
            migrationBuilder.Sql("""
                ALTER TABLE "KapatmaTahsisleri" ENABLE ROW LEVEL SECURITY;
                ALTER TABLE "KapatmaTahsisleri" FORCE ROW LEVEL SECURITY;
                CREATE POLICY tenant_isolation ON "KapatmaTahsisleri"
                    USING ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
                    WITH CHECK ("TenantId" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
                GRANT SELECT, INSERT, UPDATE, DELETE ON "KapatmaTahsisleri" TO racar_app;
                """);

            // Tahsis tutarı POZİTİF olmalı: 0/negatif bir tahsis "kapatma" değildir ve kalem
            // kalanını yanlış hesaplatırdı (uygulama zaten reddediyor; bu ikinci savunma).
            migrationBuilder.Sql("""
                ALTER TABLE "KapatmaTahsisleri"
                  ADD CONSTRAINT "CK_KapatmaTahsisleri_TutarPozitif" CHECK ("KapatilanBaz" > 0);
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "KapatmaTahsisleri");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_CashTransactions_TenantId_Id",
                table: "CashTransactions");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_AccountLedgerEntries_TenantId_Id",
                table: "AccountLedgerEntries");
        }
    }
}
