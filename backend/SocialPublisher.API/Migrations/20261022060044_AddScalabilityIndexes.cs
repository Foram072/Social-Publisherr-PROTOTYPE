using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialPublisher.API.Migrations
{
    /// <inheritdoc />
    public partial class AddScalabilityIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_SocialPostPublishes_PostId",
                table: "SocialPostPublishes");

            migrationBuilder.DropIndex(
                name: "IX_SocialConnections_ClientId",
                table: "SocialConnections");

            migrationBuilder.DropIndex(
                name: "IX_SocialAccounts_ClientId",
                table: "SocialAccounts");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_ScheduledAt",
                table: "SocialPosts",
                column: "ScheduledAt");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_Status",
                table: "SocialPosts",
                column: "Status");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPostPublishes_PostId_SocialAccountId",
                table: "SocialPostPublishes",
                columns: new[] { "PostId", "SocialAccountId" });

            migrationBuilder.CreateIndex(
                name: "IX_SocialConnections_ClientId_Provider",
                table: "SocialConnections",
                columns: new[] { "ClientId", "Provider" });

            migrationBuilder.CreateIndex(
                name: "IX_SocialAccounts_ClientId_Platform",
                table: "SocialAccounts",
                columns: new[] { "ClientId", "Platform" });

            migrationBuilder.CreateIndex(
                name: "IX_ClientConnectionInvitations_Token",
                table: "ClientConnectionInvitations",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Users_Email",
                table: "Users");

            migrationBuilder.DropIndex(
                name: "IX_SocialPosts_ScheduledAt",
                table: "SocialPosts");

            migrationBuilder.DropIndex(
                name: "IX_SocialPosts_Status",
                table: "SocialPosts");

            migrationBuilder.DropIndex(
                name: "IX_SocialPostPublishes_PostId_SocialAccountId",
                table: "SocialPostPublishes");

            migrationBuilder.DropIndex(
                name: "IX_SocialConnections_ClientId_Provider",
                table: "SocialConnections");

            migrationBuilder.DropIndex(
                name: "IX_SocialAccounts_ClientId_Platform",
                table: "SocialAccounts");

            migrationBuilder.DropIndex(
                name: "IX_ClientConnectionInvitations_Token",
                table: "ClientConnectionInvitations");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPostPublishes_PostId",
                table: "SocialPostPublishes",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialConnections_ClientId",
                table: "SocialConnections",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialAccounts_ClientId",
                table: "SocialAccounts",
                column: "ClientId");
        }
    }
}
