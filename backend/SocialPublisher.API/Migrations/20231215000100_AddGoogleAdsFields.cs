using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialPublisher.API.Migrations
{
    public partial class AddGoogleAdsFields : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "GoogleAdsRefreshToken",
                table: "SocialAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "GoogleAdsCustomerId",
                table: "SocialAccounts",
                type: "bigint",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "GoogleAdsRefreshToken",
                table: "SocialAccounts");

            migrationBuilder.DropColumn(
                name: "GoogleAdsCustomerId",
                table: "SocialAccounts");
        }
    }
}
