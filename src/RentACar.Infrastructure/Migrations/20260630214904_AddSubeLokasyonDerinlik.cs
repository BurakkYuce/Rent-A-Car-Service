using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RentACar.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddSubeLokasyonDerinlik : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CalismaSaatleri",
                table: "Locations",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Eposta",
                table: "Locations",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "TeslimUcreti",
                table: "Locations",
                type: "numeric(19,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CalismaSaatleri",
                table: "Branches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Eposta",
                table: "Branches",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "EvrakNoOnek",
                table: "Branches",
                type: "character varying(16)",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Il",
                table: "Branches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Ilce",
                table: "Branches",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "KomisyonOran",
                table: "Branches",
                type: "numeric(5,4)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Yetkili",
                table: "Branches",
                type: "character varying(128)",
                maxLength: 128,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CalismaSaatleri",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "Eposta",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "TeslimUcreti",
                table: "Locations");

            migrationBuilder.DropColumn(
                name: "CalismaSaatleri",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "Eposta",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "EvrakNoOnek",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "Il",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "Ilce",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "KomisyonOran",
                table: "Branches");

            migrationBuilder.DropColumn(
                name: "Yetkili",
                table: "Branches");
        }
    }
}
