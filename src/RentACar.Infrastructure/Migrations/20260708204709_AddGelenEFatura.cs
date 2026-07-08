using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGelenEFatura : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GelenEFaturalar",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Ettn = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    GonderenVkn = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    GonderenUnvan = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Tarih = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    NetTutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    KdvTutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    GenelToplam = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Currency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    RedNedeni = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GelenEFaturalar", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GelenEFaturalar_TenantId_Ettn",
                table: "GelenEFaturalar",
                columns: new[] { "TenantId", "Ettn" },
                unique: true);

            // RLS (tenant izolasyonu). Triage kaydı (mali belge/defter DEĞİL) → değişmezlik trigger'ı YOK; tam CRUD grant.
            migrationBuilder.Sql("ALTER TABLE \"GelenEFaturalar\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"GelenEFaturalar\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"GelenEFaturalar\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"GelenEFaturalar\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"GelenEFaturalar\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GelenEFaturalar");
        }
    }
}
