using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBookings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Rentals",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    SozlesmeNo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Durum = table.Column<int>(type: "integer", nullable: false),
                    ReservationId = table.Column<Guid>(type: "uuid", nullable: true),
                    MusteriId = table.Column<Guid>(type: "uuid", nullable: false),
                    VehicleId = table.Column<Guid>(type: "uuid", nullable: false),
                    BasTar = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    BitTar = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    CikisOfisi = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    DonusOfisi = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    CikisKm = table.Column<int>(type: "integer", nullable: true),
                    CikisYakit = table.Column<int>(type: "integer", nullable: true),
                    DonusKm = table.Column<int>(type: "integer", nullable: true),
                    DonusYakit = table.Column<int>(type: "integer", nullable: true),
                    GercekDonusTar = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    Gun = table.Column<int>(type: "integer", nullable: false),
                    GunlukUcret = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Tutar = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Tahsilat = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Bakiye = table.Column<decimal>(type: "numeric(19,4)", nullable: false),
                    Aciklama = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rentals", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Reservations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    ReservationNo = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
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
                    Aciklama = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                    RentalContractId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAtUtc = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Reservations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantSequences",
                columns: table => new
                {
                    TenantId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    NextValue = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSequences", x => new { x.TenantId, x.Name });
                });

            migrationBuilder.CreateIndex(
                name: "IX_Rentals_TenantId_SozlesmeNo",
                table: "Rentals",
                columns: new[] { "TenantId", "SozlesmeNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Rentals_TenantId_VehicleId",
                table: "Rentals",
                columns: new[] { "TenantId", "VehicleId" });

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TenantId_ReservationNo",
                table: "Reservations",
                columns: new[] { "TenantId", "ReservationNo" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Reservations_TenantId_VehicleId",
                table: "Reservations",
                columns: new[] { "TenantId", "VehicleId" });

            // ---------------------------------------------------------------
            // Double-booking koruması (DB-seviyesi, defense-in-depth): aktif (Kirada=0)
            // kira sözleşmeleri için aynı tenant+araç'ta çakışan tarih aralığı yasak.
            // Generated tstzrange [BasTar, BitTar) + GiST exclusion constraint.
            // btree_gist trusted extension (racar_owner DB sahibi → kurabilir).
            // ---------------------------------------------------------------
            migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");
            migrationBuilder.Sql(
                "ALTER TABLE \"Rentals\" ADD COLUMN \"Period\" tstzrange " +
                "GENERATED ALWAYS AS (tstzrange(\"BasTar\", \"BitTar\")) STORED;");
            migrationBuilder.Sql(
                "ALTER TABLE \"Rentals\" ADD CONSTRAINT rentals_no_overlap " +
                "EXCLUDE USING gist (\"TenantId\" WITH =, \"VehicleId\" WITH =, \"Period\" WITH &&) " +
                "WHERE (\"Durum\" = 0);");

            // RLS (tenant izolasyonu) — Reservations, Rentals, TenantSequences.
            foreach (var table in new[] { "Reservations", "Rentals", "TenantSequences" })
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
                name: "Rentals");

            migrationBuilder.DropTable(
                name: "Reservations");

            migrationBuilder.DropTable(
                name: "TenantSequences");
        }
    }
}
