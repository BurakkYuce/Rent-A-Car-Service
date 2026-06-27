using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceRecords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ServiceRecords",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    No = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    Tip = table.Column<int>(type: "integer", nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    AtolyeAdi = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                    GirisTarihi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CikisTarihi = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    GirisKm = table.Column<int>(type: "integer", nullable: false),
                    CikisKm = table.Column<int>(type: "integer", nullable: true),
                    HasarSorumlu = table.Column<int>(type: "integer", nullable: false),
                    KusurOrani = table.Column<decimal>(type: "numeric(5,4)", nullable: true),
                    SonrakiBakimKm = table.Column<int>(type: "integer", nullable: true),
                    Aciklama = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    ToplamIscilik = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceRecords", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceLines",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ServiceRecordId = table.Column<Guid>(type: "uuid", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Tutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceLines_ServiceRecords_ServiceRecordId",
                        column: x => x.ServiceRecordId,
                        principalTable: "ServiceRecords",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceLines_ServiceRecordId",
                table: "ServiceLines",
                column: "ServiceRecordId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceLines_TenantId_ServiceRecordId",
                table: "ServiceLines",
                columns: new[] { "TenantId", "ServiceRecordId" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRecords_TenantId_Durum",
                table: "ServiceRecords",
                columns: new[] { "TenantId", "Durum" });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRecords_TenantId_No",
                table: "ServiceRecords",
                columns: new[] { "TenantId", "No" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRecords_TenantId_VehicleId",
                table: "ServiceRecords",
                columns: new[] { "TenantId", "VehicleId" });

            // RLS (tenant izolasyonu). Operasyonel kayıtlar (mali belge DEĞİL) → değişmezlik
            // trigger'ı YOK; tam CRUD grant. ServiceLines kalemleri de aynı.
            foreach (var table in new[] { "ServiceRecords", "ServiceLines" })
            {
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" ENABLE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"ALTER TABLE \"{table}\" FORCE ROW LEVEL SECURITY;");
                migrationBuilder.Sql($"DROP POLICY IF EXISTS tenant_isolation ON \"{table}\";");
                migrationBuilder.Sql(
                    $"CREATE POLICY tenant_isolation ON \"{table}\" " +
                    "USING (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid) " +
                    "WITH CHECK (\"TenantId\" = NULLIF(current_setting('app.tenant_id', true), '')::uuid);");
                migrationBuilder.Sql($"GRANT SELECT, INSERT, UPDATE, DELETE ON \"{table}\" TO racar_app;");
            }
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceLines");

            migrationBuilder.DropTable(
                name: "ServiceRecords");
        }
    }
}
