using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddExpenses : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Expenses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    No = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Tip = table.Column<int>(type: "integer", nullable: false),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    CariId = table.Column<Guid>(type: "uuid", nullable: true),
                    Sube = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    EvrakNo = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    NetTutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    KdvOrani = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    KdvTutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    GenelToplam = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Kur = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    OdemeYontemi = table.Column<int>(type: "integer", nullable: false),
                    KasaBankaHesap = table.Column<int>(type: "integer", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Expenses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TenantId_No",
                table: "Expenses",
                columns: new[] { "TenantId", "No" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_TenantId_VehicleId",
                table: "Expenses",
                columns: new[] { "TenantId", "VehicleId" });

            // RLS + DEĞİŞMEZLİK: gider de mali kayıttır (rc_prevent_mutation AddCashAndLedger'da).
            migrationBuilder.Sql("ALTER TABLE \"Expenses\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"Expenses\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"Expenses\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"Expenses\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT ON \"Expenses\" TO racar_app;");
            migrationBuilder.Sql(
                "CREATE TRIGGER expenses_immutable BEFORE UPDATE OR DELETE ON \"Expenses\" " +
                "FOR EACH ROW EXECUTE FUNCTION rc_prevent_mutation();");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS expenses_immutable ON \"Expenses\";");
            migrationBuilder.DropTable(
                name: "Expenses");
        }
    }
}
