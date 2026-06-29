using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddFiloKiralama : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FiloKiralamalar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    No = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MusteriId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    BasTar = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SureAy = table.Column<int>(type: "integer", nullable: false),
                    AylikUcret = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    KdvOrani = table.Column<decimal>(type: "numeric(9,4)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Kur = table.Column<decimal>(type: "numeric(19,6)", nullable: false),
                    ToplamKmLimiti = table.Column<int>(type: "integer", nullable: true),
                    DamgaVergisi = table.Column<decimal>(type: "numeric(19,4)", nullable: true),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FiloKiralamalar", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_FiloKiralamalar_TenantId_MusteriId",
                table: "FiloKiralamalar",
                columns: new[] { "TenantId", "MusteriId" });

            migrationBuilder.CreateIndex(
                name: "IX_FiloKiralamalar_TenantId_No",
                table: "FiloKiralamalar",
                columns: new[] { "TenantId", "No" },
                unique: true);

            // roadmap L1: RLS — tenant izolasyonu (ELLE; EF üretmez). Full-CRUD (mali değişmez belge DEĞİL).
            migrationBuilder.Sql("ALTER TABLE \"FiloKiralamalar\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"FiloKiralamalar\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"FiloKiralamalar\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"FiloKiralamalar\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"FiloKiralamalar\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FiloKiralamalar");
        }
    }
}
