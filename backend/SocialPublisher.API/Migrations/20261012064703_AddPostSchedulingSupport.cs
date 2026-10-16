using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialPublisher.API.Migrations
{
    /// <inheritdoc />
    public partial class AddPostSchedulingSupport : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ScheduledAt",
                table: "SocialPosts",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ScheduledPlatforms",
                table: "SocialPosts",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ScheduledAt",
                table: "SocialPosts");

            migrationBuilder.DropColumn(
                name: "ScheduledPlatforms",
                table: "SocialPosts");
        }
    }
}
