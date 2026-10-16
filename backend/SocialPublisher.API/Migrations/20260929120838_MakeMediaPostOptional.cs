using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SocialPublisher.API.Migrations
{
    /// <inheritdoc />
    public partial class MakeMediaPostOptional : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SocialPostMedia_SocialPosts_PostId",
                table: "SocialPostMedia");

            migrationBuilder.AlterColumn<int>(
                name: "PostId",
                table: "SocialPostMedia",
                type: "integer",
                nullable: true,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AddForeignKey(
                name: "FK_SocialPostMedia_SocialPosts_PostId",
                table: "SocialPostMedia",
                column: "PostId",
                principalTable: "SocialPosts",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SocialPostMedia_SocialPosts_PostId",
                table: "SocialPostMedia");

            migrationBuilder.AlterColumn<int>(
                name: "PostId",
                table: "SocialPostMedia",
                type: "integer",
                nullable: false,
                defaultValue: 0,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "FK_SocialPostMedia_SocialPosts_PostId",
                table: "SocialPostMedia",
                column: "PostId",
                principalTable: "SocialPosts",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
