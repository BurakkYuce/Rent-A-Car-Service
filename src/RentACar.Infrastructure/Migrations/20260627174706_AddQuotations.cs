using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddQuotations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Quotations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    No = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    MusteriId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    BasTar = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BitTar = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CikisOfisi = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DonusOfisi = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    Gun = table.Column<int>(type: "integer", nullable: false),
                    GunlukUcret = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Tutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    KmLimit = table.Column<int>(type: "integer", nullable: false),
                    FazlaKmUcret = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    YakitBirimUcret = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    GecerlilikTarihi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Aciklama = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Quotations", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_TenantId_Durum",
                table: "Quotations",
                columns: new[] { "TenantId", "Durum" });

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_TenantId_No",
                table: "Quotations",
                columns: new[] { "TenantId", "No" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Quotations_TenantId_VehicleId",
                table: "Quotations",
                columns: new[] { "TenantId", "VehicleId" });

            // RLS (tenant izolasyonu). Teklif operasyonel kayıttır (mali belge DEĞİL) →
            // değişmezlik trigger'ı YOK; tam CRUD grant (durum geçişleri + kabul güncellemesi).
            migrationBuilder.Sql("ALTER TABLE \"Quotations\" ENABLE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("ALTER TABLE \"Quotations\" FORCE ROW LEVEL SECURITY;");
            migrationBuilder.Sql("DROP POLICY IF EXISTS tenant_isolation ON \"Quotations\";");
            migrationBuilder.Sql(
                "CREATE POLICY tenant_isolation ON \"Quotations\" " +
                "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
            migrationBuilder.Sql("GRANT SELECT, INSERT, UPDATE, DELETE ON \"Quotations\" TO racar_app;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Quotations");
        }
    }
}
