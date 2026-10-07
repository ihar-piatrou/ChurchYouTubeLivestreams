using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChurchYouTubeAssistant.Migrations
{
    /// <inheritdoc />
    public partial class AddVideoAnalysis : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "VideoAnalyses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    VideoId = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OriginalTitle = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    OriginalDescription = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Language = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: true),
                    VideoDurationSeconds = table.Column<int>(type: "int", nullable: true),
                    OptimizedTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    OptimizedDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    AlternativeTitles = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    TitleReasoning = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MainMessage = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    MainQuestion = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    KeyThemes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Keywords = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    EditedTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    EditedDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    RawOpenAiResponse = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Model = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    PromptVersion = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    PromptTokens = table.Column<int>(type: "int", nullable: true),
                    CompletionTokens = table.Column<int>(type: "int", nullable: true),
                    TotalTokens = table.Column<int>(type: "int", nullable: true),
                    UsedMultiStageFallback = table.Column<bool>(type: "bit", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    IsPublished = table.Column<bool>(type: "bit", nullable: false),
                    PublishedAtUtc = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: true),
                    PublishedTitle = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    PublishedDescription = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    BibleReferences = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Chapters = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Shorts = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    Thumbnail = table.Column<string>(type: "nvarchar(max)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VideoAnalyses", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_VideoAnalyses_VideoId_CreatedAtUtc",
                table: "VideoAnalyses",
                columns: new[] { "VideoId", "CreatedAtUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "VideoAnalyses");
        }
    }
}
