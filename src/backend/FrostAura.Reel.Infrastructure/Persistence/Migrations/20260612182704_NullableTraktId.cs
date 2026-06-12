using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FrostAura.Reel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class NullableTraktId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Titles_MediaType_TraktId",
                table: "Titles");

            migrationBuilder.AlterColumn<long>(
                name: "TraktId",
                table: "Titles",
                type: "bigint",
                nullable: true,
                oldClrType: typeof(long),
                oldType: "bigint");

            migrationBuilder.CreateIndex(
                name: "IX_Titles_MediaType_TraktId",
                table: "Titles",
                columns: new[] { "MediaType", "TraktId" },
                unique: true,
                filter: "\"TraktId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Titles_MediaType_TraktId",
                table: "Titles");

            migrationBuilder.AlterColumn<long>(
                name: "TraktId",
                table: "Titles",
                type: "bigint",
                nullable: false,
                defaultValue: 0L,
                oldClrType: typeof(long),
                oldType: "bigint",
                oldNullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Titles_MediaType_TraktId",
                table: "Titles",
                columns: new[] { "MediaType", "TraktId" },
                unique: true);
        }
    }
}
