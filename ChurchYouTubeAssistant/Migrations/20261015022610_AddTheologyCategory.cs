using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ChurchYouTubeAssistant.Migrations
{
    /// <inheritdoc />
    public partial class AddTheologyCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PrimaryCategory",
                table: "VideoAnalyses",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SecondaryCategories",
                table: "VideoAnalyses",
                type: "nvarchar(max)",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PrimaryCategory",
                table: "VideoAnalyses");

            migrationBuilder.DropColumn(
                name: "SecondaryCategories",
                table: "VideoAnalyses");
        }
    }
}
