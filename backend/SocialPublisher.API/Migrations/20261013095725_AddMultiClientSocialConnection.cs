using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SocialPublisher.API.Migrations
{
    /// <inheritdoc />
    public partial class AddMultiClientSocialConnection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "ClientId",
                table: "SocialPosts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "AvatarUrl",
                table: "SocialAccounts",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ClientId",
                table: "SocialAccounts",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ConnectionId",
                table: "SocialAccounts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SocialClients",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    TimeZone = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialClients", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SocialConnections",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientId = table.Column<int>(type: "integer", nullable: true),
                    Provider = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    ProviderUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ProviderUserName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    ProviderUserEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                    AccessToken = table.Column<string>(type: "text", nullable: false),
                    RefreshToken = table.Column<string>(type: "text", nullable: true),
                    TokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    GrantedScopes = table.Column<string>(type: "text", nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    OAuthState = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialConnections", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialConnections_SocialClients_ClientId",
                        column: x => x.ClientId,
                        principalTable: "SocialClients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialPosts_ClientId",
                table: "SocialPosts",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialAccounts_ClientId",
                table: "SocialAccounts",
                column: "ClientId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialAccounts_ConnectionId",
                table: "SocialAccounts",
                column: "ConnectionId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialConnections_ClientId",
                table: "SocialConnections",
                column: "ClientId");

            migrationBuilder.AddForeignKey(
                name: "FK_SocialAccounts_SocialClients_ClientId",
                table: "SocialAccounts",
                column: "ClientId",
                principalTable: "SocialClients",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SocialAccounts_SocialConnections_ConnectionId",
                table: "SocialAccounts",
                column: "ConnectionId",
                principalTable: "SocialConnections",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);

            migrationBuilder.AddForeignKey(
                name: "FK_SocialPosts_SocialClients_ClientId",
                table: "SocialPosts",
                column: "ClientId",
                principalTable: "SocialClients",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SocialAccounts_SocialClients_ClientId",
                table: "SocialAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_SocialAccounts_SocialConnections_ConnectionId",
                table: "SocialAccounts");

            migrationBuilder.DropForeignKey(
                name: "FK_SocialPosts_SocialClients_ClientId",
                table: "SocialPosts");

            migrationBuilder.DropTable(
                name: "SocialConnections");

            migrationBuilder.DropTable(
                name: "SocialClients");

            migrationBuilder.DropIndex(
                name: "IX_SocialPosts_ClientId",
                table: "SocialPosts");

            migrationBuilder.DropIndex(
                name: "IX_SocialAccounts_ClientId",
                table: "SocialAccounts");

            migrationBuilder.DropIndex(
                name: "IX_SocialAccounts_ConnectionId",
                table: "SocialAccounts");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "AvatarUrl",
                table: "SocialAccounts");

            migrationBuilder.DropColumn(
                name: "ClientId",
                table: "SocialAccounts");

            migrationBuilder.DropColumn(
                name: "ConnectionId",
                table: "SocialAccounts");
        }
    }
}
