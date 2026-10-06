using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuestencode.Beetwerk.Data.Migrations
{
    /// <inheritdoc />
    public partial class Version2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<double>(
                name: "NeighborWarningDistance",
                table: "Gardens",
                type: "REAL",
                nullable: false,
                defaultValue: 1.0);

            migrationBuilder.CreateTable(
                name: "MapOverlays",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    CornersJson = table.Column<string>(type: "TEXT", nullable: false),
                    Opacity = table.Column<double>(type: "REAL", nullable: false),
                    Visible = table.Column<bool>(type: "INTEGER", nullable: false),
                    UpdatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MapOverlays", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ObjectPhotos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ObjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    FileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ThumbnailFileName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    ContentType = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Caption = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    TakenOn = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectPhotos", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ObjectPhotos_Objects_ObjectId",
                        column: x => x.ObjectId,
                        principalTable: "Objects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SpeciesTaskTemplates",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    PlantSpeciesId = table.Column<int>(type: "INTEGER", nullable: false),
                    Title = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Frequency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    Interval = table.Column<int>(type: "INTEGER", nullable: false),
                    SeasonStartMonth = table.Column<int>(type: "INTEGER", nullable: true),
                    SeasonEndMonth = table.Column<int>(type: "INTEGER", nullable: true),
                    StartMonth = table.Column<int>(type: "INTEGER", nullable: true),
                    StartDay = table.Column<int>(type: "INTEGER", nullable: false),
                    Notify = table.Column<bool>(type: "INTEGER", nullable: false),
                    LeadDays = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SpeciesTaskTemplates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SpeciesTaskTemplates_PlantSpecies_PlantSpeciesId",
                        column: x => x.PlantSpeciesId,
                        principalTable: "PlantSpecies",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ObjectLog",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ObjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Date = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Kind = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    Text = table.Column<string>(type: "TEXT", nullable: false),
                    TaskId = table.Column<int>(type: "INTEGER", nullable: true),
                    PhotoId = table.Column<int>(type: "INTEGER", nullable: true),
                    CreatedBy = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    CreatedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ObjectLog", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ObjectLog_ObjectPhotos_PhotoId",
                        column: x => x.PhotoId,
                        principalTable: "ObjectPhotos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ObjectLog_Objects_ObjectId",
                        column: x => x.ObjectId,
                        principalTable: "Objects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectLog_ObjectId_Date",
                table: "ObjectLog",
                columns: new[] { "ObjectId", "Date" });

            migrationBuilder.CreateIndex(
                name: "IX_ObjectLog_PhotoId",
                table: "ObjectLog",
                column: "PhotoId");

            migrationBuilder.CreateIndex(
                name: "IX_ObjectLog_TaskId",
                table: "ObjectLog",
                column: "TaskId");

            migrationBuilder.CreateIndex(
                name: "IX_ObjectPhotos_ObjectId",
                table: "ObjectPhotos",
                column: "ObjectId");

            migrationBuilder.CreateIndex(
                name: "IX_SpeciesTaskTemplates_PlantSpeciesId",
                table: "SpeciesTaskTemplates",
                column: "PlantSpeciesId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "MapOverlays");

            migrationBuilder.DropTable(
                name: "ObjectLog");

            migrationBuilder.DropTable(
                name: "SpeciesTaskTemplates");

            migrationBuilder.DropTable(
                name: "ObjectPhotos");

            migrationBuilder.DropColumn(
                name: "NeighborWarningDistance",
                table: "Gardens");
        }
    }
}
