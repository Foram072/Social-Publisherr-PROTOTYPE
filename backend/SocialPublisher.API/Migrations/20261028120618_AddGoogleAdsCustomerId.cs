using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialPublisher.API.Migrations
{
    /// <inheritdoc />
    public partial class AddGoogleAdsCustomerId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "GoogleAdsCustomerId",
                table: "SocialAccounts",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GoogleAdsRefreshToken",
                table: "SocialAccounts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleAdsCustomerId",
                table: "SocialAccounts");

            migrationBuilder.DropColumn(
                name: "GoogleAdsRefreshToken",
                table: "SocialAccounts");
        }
    }
}
