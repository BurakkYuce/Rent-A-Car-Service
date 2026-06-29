using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAracSiparis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AracSiparisleri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    No = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Tedarikci = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SiparisTarihi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BeklenenTeslim = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Marka = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Tip = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Grup = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Adet = table.Column<int>(type: "integer", nullable: false),
                    BirimFiyat = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Kur = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AracSiparisleri", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AracSiparisleri_TenantId_No",
                table: "AracSiparisleri",
                columns: new[] { "TenantId", "No" },
                unique: true);

            // roadmap L3: RLS — tenant izolasyonu (ELLE). Full-CRUD (mali değişmez belge DEĞİL).
            migrationBuilder.Sql("ALTER TABLE \"AracSiparisleri\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"AracSiparisleri\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"AracSiparisleri\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"AracSiparisleri\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"AracSiparisleri\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AracSiparisleri");
        }
    }
}
