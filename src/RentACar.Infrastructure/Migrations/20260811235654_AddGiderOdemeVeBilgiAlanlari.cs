using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGiderOdemeVeBilgiAlanlari : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "HazirAciklama",
                table: "Expenses",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "OdemeTarihi",
                table: "Expenses",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "RentalId",
                table: "Expenses",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "GiderOdemeleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ExpenseId = table.Column<Guid>(type: "uuid", nullable: false),
                    Sira = table.Column<int>(type: "integer", nullable: false),
                    Tutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    KalanSonrasi = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    MakbuzNo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    IslemYapan = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    Anahtar = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    IslemAnahtari = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GiderOdemeleri", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GiderOdemeleri_TenantId_Anahtar",
                table: "GiderOdemeleri",
                columns: new[] { "TenantId", "Anahtar" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_GiderOdemeleri_TenantId_ExpenseId",
                table: "GiderOdemeleri",
                columns: new[] { "TenantId", "ExpenseId" });

            migrationBuilder.CreateIndex(
                name: "IX_GiderOdemeleri_TenantId_IslemAnahtari",
                table: "GiderOdemeleri",
                columns: new[] { "TenantId", "IslemAnahtari" },
                unique: true,
                filter: "\"IslemAnahtari\" IS NOT NULL");

            // RLS (tenant izolasyonu) — EF üretmez, CLAUDE.md §5 gereği ELLE yazılır.
            migrationBuilder.Sql("ALTER TABLE \"GiderOdemeleri\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"GiderOdemeleri\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"GiderOdemeleri\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"GiderOdemeleri\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");

            // DEĞİŞMEZLİK: ödeme kaydı mali izdir — düzeltme yeni kayıtla yapılır, satır GÜNCELLENMEZ.
            // Bu yüzden yalnız SELECT + INSERT grant edilir ve UPDATE/DELETE trigger'la da engellenir
            // (uygulama hatalı olsa bile DB tutar).
            migrationBuilder.Sql("GRANT SELECT, INSERT ON \"GiderOdemeleri\" TO racar_app;");
            migrationBuilder.Sql(
                "CREATE TRIGGER gider_odemeleri_immutable BEFORE UPDATE OR DELETE ON \"GiderOdemeleri\" " +
                "FOR EACH ROW EXECUTE FUNCTION rc_prevent_mutation();");

            // Ödeme tutarı POZİTİF olmalı (uygulama guard'ıyla aynı sınır, DB'de de tutulur).
            migrationBuilder.Sql(
                "ALTER TABLE \"GiderOdemeleri\" ADD CONSTRAINT ck_gider_odeme_tutar_pozitif CHECK (\"Tutar\" > 0);");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS gider_odemeleri_immutable ON \"GiderOdemeleri\";");
            migrationBuilder.DropTable(
                name: "GiderOdemeleri");

            migrationBuilder.DropColumn(
                name: "HazirAciklama",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "OdemeTarihi",
                table: "Expenses");

            migrationBuilder.DropColumn(
                name: "RentalId",
                table: "Expenses");
        }
    }
}
