using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <summary>
    /// FAZ-55 — GelenEFaturalar'a KDV oran kırılımı (%20/%10/%1/%0 matrah+KDV), araç/gider-kategori/
    /// cari bağı ve giderleştirme izi kolonları.
    ///
    /// <para><b>RLS:</b> ELLE BLOK YOK ve GEREKMEZ — mevcut tenant-owned tabloya ADDITIVE kolon
    /// eklenmektedir; <c>GelenEFaturalar</c> üzerinde ENABLE+FORCE ROW LEVEL SECURITY, tenant_isolation
    /// policy ve racar_app grant'ları AddGelenEFatura (20260708204709) migration'ında zaten kurulu.
    /// Policy tablo düzeyindedir, kolon eklemek onu etkilemez.</para>
    ///
    /// <para><b>BACKFILL YOK (bilinçli):</b> tüm yeni kolonlar NULLABLE. Non-null + defaultValue 0
    /// olsaydı geçmiş satırlara "matrahı 0", "KDV'si 0", hatta GiderTipi için "Genel" anlamı
    /// yüklenirdi — hâlbuki bu bilgiler o satırlarda BİLİNMİYOR. null = "girilmemiş",
    /// 0 = "gerçekten sıfır" ayrımı korunur. (RLS backfill tuzağı da böylece hiç doğmaz.)</para>
    /// </summary>
    public partial class AddGelenEFaturaKdvKirilimVeBaglama : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "CariId",
                table: "GelenEFaturalar",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "ExpenseCategoryId",
                table: "GelenEFaturalar",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "GiderIslemAnahtari",
                table: "GelenEFaturalar",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "GiderTipi",
                table: "GelenEFaturalar",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "GiderlestirilmeUtc",
                table: "GelenEFaturalar",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Kdv0Matrah",
                table: "GelenEFaturalar",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Kdv1",
                table: "GelenEFaturalar",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Kdv10",
                table: "GelenEFaturalar",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Kdv10Matrah",
                table: "GelenEFaturalar",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Kdv1Matrah",
                table: "GelenEFaturalar",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Kdv20",
                table: "GelenEFaturalar",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "Kdv20Matrah",
                table: "GelenEFaturalar",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "VehicleId",
                table: "GelenEFaturalar",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_GelenEFaturalar_TenantId_VehicleId",
                table: "GelenEFaturalar",
                columns: new[] { "TenantId", "VehicleId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_GelenEFaturalar_TenantId_VehicleId",
                table: "GelenEFaturalar");

            migrationBuilder.DropColumn(
                name: "CariId",
                table: "GelenEFaturalar");

            migrationBuilder.DropColumn(
                name: "ExpenseCategoryId",
                table: "GelenEFaturalar");

            migrationBuilder.DropColumn(
                name: "GiderIslemAnahtari",
                table: "GelenEFaturalar");

            migrationBuilder.DropColumn(
                name: "GiderTipi",
                table: "GelenEFaturalar");

            migrationBuilder.DropColumn(
                name: "GiderlestirilmeUtc",
                table: "GelenEFaturalar");

            migrationBuilder.DropColumn(
                name: "Kdv0Matrah",
                table: "GelenEFaturalar");

            migrationBuilder.DropColumn(
                name: "Kdv1",
                table: "GelenEFaturalar");

            migrationBuilder.DropColumn(
                name: "Kdv10",
                table: "GelenEFaturalar");

            migrationBuilder.DropColumn(
                name: "Kdv10Matrah",
                table: "GelenEFaturalar");

            migrationBuilder.DropColumn(
                name: "Kdv1Matrah",
                table: "GelenEFaturalar");

            migrationBuilder.DropColumn(
                name: "Kdv20",
                table: "GelenEFaturalar");

            migrationBuilder.DropColumn(
                name: "Kdv20Matrah",
                table: "GelenEFaturalar");

            migrationBuilder.DropColumn(
                name: "VehicleId",
                table: "GelenEFaturalar");
        }
    }
}
