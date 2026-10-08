using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChurchYouTubeAssistant.Migrations
{
    /// <inheritdoc />
    public partial class AddThumbnailImage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "EditedThumbnailPrompt",
                table: "VideoAnalyses",
                type: "nvarchar(max)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ThumbnailImageContentType",
                table: "VideoAnalyses",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "ThumbnailImageData",
                table: "VideoAnalyses",
                type: "varbinary(max)",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ThumbnailImageGeneratedAtUtc",
                table: "VideoAnalyses",
                type: "datetimeoffset",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "ThumbnailPublished",
                table: "VideoAnalyses",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ThumbnailPublishedAtUtc",
                table: "VideoAnalyses",
                type: "datetimeoffset",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "EditedThumbnailPrompt",
                table: "VideoAnalyses");

            migrationBuilder.DropColumn(
                name: "ThumbnailImageContentType",
                table: "VideoAnalyses");

            migrationBuilder.DropColumn(
                name: "ThumbnailImageData",
                table: "VideoAnalyses");

            migrationBuilder.DropColumn(
                name: "ThumbnailImageGeneratedAtUtc",
                table: "VideoAnalyses");

            migrationBuilder.DropColumn(
                name: "ThumbnailPublished",
                table: "VideoAnalyses");

            migrationBuilder.DropColumn(
                name: "ThumbnailPublishedAtUtc",
                table: "VideoAnalyses");
        }
    }
}
