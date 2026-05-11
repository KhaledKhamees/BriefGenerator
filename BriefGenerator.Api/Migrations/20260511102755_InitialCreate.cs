using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BriefGenerator.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(256)", nullable: false),
                    PasswordHash = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "IntakeSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    UserId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Title = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(50)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_IntakeSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_IntakeSessions_Users_UserId",
                        column: x => x.UserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Briefs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IntakeSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    StructuredJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MarkdownOutput = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    MissingFieldsJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Briefs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Briefs_IntakeSessions_IntakeSessionId",
                        column: x => x.IntakeSessionId,
                        principalTable: "IntakeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UploadedFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    IntakeSessionId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    FileType = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    StoragePath = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ExtractedText = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UploadedFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UploadedFiles_IntakeSessions_IntakeSessionId",
                        column: x => x.IntakeSessionId,
                        principalTable: "IntakeSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClientResponses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BriefId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    FieldName = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    ResponseText = table.Column<string>(type: "nvarchar(max)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientResponses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientResponses_Briefs_BriefId",
                        column: x => x.BriefId,
                        principalTable: "Briefs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ShareLinks",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    BriefId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    Token = table.Column<string>(type: "nvarchar(512)", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ShareLinks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ShareLinks_Briefs_BriefId",
                        column: x => x.BriefId,
                        principalTable: "Briefs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Briefs_IntakeSessionId",
                table: "Briefs",
                column: "IntakeSessionId",
                unique:true);

            migrationBuilder.CreateIndex(
                name: "IX_ClientResponses_BriefId",
                table: "ClientResponses",
                column: "BriefId");

            migrationBuilder.CreateIndex(
                name: "IX_IntakeSessions_UserId",
                table: "IntakeSessions",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_ShareLinks_BriefId",
                table: "ShareLinks",
                column: "BriefId");

            migrationBuilder.CreateIndex(
                name: "IX_UploadedFiles_IntakeSessionId",
                table: "UploadedFiles",
                column: "IntakeSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_Users_Email",
                table: "Users",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ShareLinks_Token",
                table: "ShareLinks",
                column: "Token",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClientResponses");

            migrationBuilder.DropTable(
                name: "ShareLinks");

            migrationBuilder.DropTable(
                name: "UploadedFiles");

            migrationBuilder.DropTable(
                name: "Briefs");

            migrationBuilder.DropTable(
                name: "IntakeSessions");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
