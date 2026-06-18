using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FrostAura.Reel.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AccountStripeLinkage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "StripeCustomerId",
                table: "Accounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StripeSubscriptionId",
                table: "Accounts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "StripeCustomerId",
                table: "Accounts");

            migrationBuilder.DropColumn(
                name: "StripeSubscriptionId",
                table: "Accounts");
        }
    }
}
