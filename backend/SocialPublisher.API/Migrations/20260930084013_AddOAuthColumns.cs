using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialPublisher.API.Migrations
{
    /// <inheritdoc />
    public partial class AddOAuthColumns : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OAuthState",
                table: "SocialAccounts",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RefreshToken",
                table: "SocialAccounts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OAuthState",
                table: "SocialAccounts");

            migrationBuilder.DropColumn(
                name: "RefreshToken",
                table: "SocialAccounts");
        }
    }
}
