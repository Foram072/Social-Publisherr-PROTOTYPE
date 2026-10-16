using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SocialPublisher.API.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SocialAccounts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Platform = table.Column<string>(type: "text", nullable: false),
                    AccountName = table.Column<string>(type: "text", nullable: false),
                    ExternalAccountId = table.Column<string>(type: "text", nullable: false),
                    AccessToken = table.Column<string>(type: "text", nullable: false),
                    TokenExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsConnected = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialAccounts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SocialPosts",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Title = table.Column<string>(type: "text", nullable: false),
                    Content = table.Column<string>(type: "text", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialPosts", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SocialPostMedia",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PostId = table.Column<int>(type: "integer", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    FileUrl = table.Column<string>(type: "text", nullable: false),
                    MediaType = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialPostMedia", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialPostMedia_SocialPosts_PostId",
                        column: x => x.PostId,
                        principalTable: "SocialPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SocialPostPublishes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PostId = table.Column<int>(type: "integer", nullable: false),
                    SocialAccountId = table.Column<int>(type: "integer", nullable: false),
                    Status = table.Column<string>(type: "text", nullable: false),
                    ExternalPostId = table.Column<string>(type: "text", nullable: true),
                    PostUrl = table.Column<string>(type: "text", nullable: true),
                    ErrorMessage = table.Column<string>(type: "text", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SocialPostPublishes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SocialPostPublishes_SocialAccounts_SocialAccountId",
                        column: x => x.SocialAccountId,
                        principalTable: "SocialAccounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SocialPostPublishes_SocialPosts_PostId",
                        column: x => x.PostId,
                        principalTable: "SocialPosts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SocialPostMedia_PostId",
                table: "SocialPostMedia",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPostPublishes_PostId",
                table: "SocialPostPublishes",
                column: "PostId");

            migrationBuilder.CreateIndex(
                name: "IX_SocialPostPublishes_SocialAccountId",
                table: "SocialPostPublishes",
                column: "SocialAccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SocialPostMedia");

            migrationBuilder.DropTable(
                name: "SocialPostPublishes");

            migrationBuilder.DropTable(
                name: "SocialAccounts");

            migrationBuilder.DropTable(
                name: "SocialPosts");
        }
    }
}
