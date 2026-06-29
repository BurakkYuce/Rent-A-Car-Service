using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAracKredi : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AracKredileri",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    No = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    BankaAdi = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: true),
                    KrediTutari = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    FaizOran = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    TaksitSayisi = table.Column<int>(type: "integer", nullable: false),
                    BaslangicTarihi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OdenenTaksit = table.Column<int>(type: "integer", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Kur = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AracKredileri", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AracKredileri_TenantId_No",
                table: "AracKredileri",
                columns: new[] { "TenantId", "No" },
                unique: true);

            // roadmap L4: RLS — tenant izolasyonu (ELLE). Full-CRUD (mali değişmez belge DEĞİL).
            migrationBuilder.Sql("ALTER TABLE \"AracKredileri\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"AracKredileri\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"AracKredileri\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"AracKredileri\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"AracKredileri\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AracKredileri");
        }
    }
}
