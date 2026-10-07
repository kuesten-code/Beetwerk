using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Kuestencode.Beetwerk.Data.Migrations
{
    /// <inheritdoc />
    public partial class DeviceLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DeviceLinks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ObjectId = table.Column<int>(type: "INTEGER", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ExternalId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Settings = table.Column<string>(type: "TEXT", nullable: false),
                    CreateTasksOnError = table.Column<bool>(type: "INTEGER", nullable: false),
                    ErrorTaskId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DeviceLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DeviceLinks_Objects_ObjectId",
                        column: x => x.ObjectId,
                        principalTable: "Objects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DeviceLinks_ObjectId",
                table: "DeviceLinks",
                column: "ObjectId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DeviceLinks");
        }
    }
}
