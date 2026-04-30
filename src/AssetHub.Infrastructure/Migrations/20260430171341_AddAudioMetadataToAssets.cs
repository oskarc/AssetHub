using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AssetHub.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddAudioMetadataToAssets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "AudioBitrateKbps",
                table: "Assets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudioChannels",
                table: "Assets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "AudioSampleRateHz",
                table: "Assets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "DurationSeconds",
                table: "Assets",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "WaveformPeaksPath",
                table: "Assets",
                type: "character varying(512)",
                maxLength: 512,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioBitrateKbps",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "AudioChannels",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "AudioSampleRateHz",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "DurationSeconds",
                table: "Assets");

            migrationBuilder.DropColumn(
                name: "WaveformPeaksPath",
                table: "Assets");
        }
    }
}
