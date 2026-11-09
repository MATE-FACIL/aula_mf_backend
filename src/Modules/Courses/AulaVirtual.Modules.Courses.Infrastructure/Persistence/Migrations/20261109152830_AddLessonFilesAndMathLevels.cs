using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AulaVirtual.Modules.Courses.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddLessonFilesAndMathLevels : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "video_content_type",
                schema: "courses",
                table: "lessons",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "video_file_id",
                schema: "courses",
                table: "lessons",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "video_size",
                schema: "courses",
                table: "lessons",
                type: "bigint",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "url",
                schema: "courses",
                table: "lesson_materials",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048);

            migrationBuilder.AddColumn<string>(
                name: "content_type",
                schema: "courses",
                table: "lesson_materials",
                type: "character varying(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "file_id",
                schema: "courses",
                table: "lesson_materials",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "size",
                schema: "courses",
                table: "lesson_materials",
                type: "bigint",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "math_level_courses",
                schema: "courses",
                columns: table => new
                {
                    stage = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    level = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_math_level_courses", x => new { x.stage, x.level });
                    table.CheckConstraint("ck_math_level_courses_level", "level IN ('prebasic', 'basic', 'intermediate', 'advanced')");
                    table.CheckConstraint("ck_math_level_courses_stage", "stage IN ('primary', 'secondary')");
                    table.ForeignKey(
                        name: "fk_math_level_courses_courses_course_id",
                        column: x => x.course_id,
                        principalSchema: "courses",
                        principalTable: "courses",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.AddCheckConstraint(
                name: "ck_lessons_video_link_or_file",
                schema: "courses",
                table: "lessons",
                sql: "video_url IS NULL OR video_file_id IS NULL");

            migrationBuilder.AddCheckConstraint(
                name: "ck_lesson_materials_link_or_file",
                schema: "courses",
                table: "lesson_materials",
                sql: "(url IS NOT NULL AND file_id IS NULL) OR (url IS NULL AND file_id IS NOT NULL AND content_type IS NOT NULL AND size > 0)");

            migrationBuilder.CreateIndex(
                name: "ux_math_level_courses_course",
                schema: "courses",
                table: "math_level_courses",
                column: "course_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "math_level_courses",
                schema: "courses");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lessons_video_link_or_file",
                schema: "courses",
                table: "lessons");

            migrationBuilder.DropCheckConstraint(
                name: "ck_lesson_materials_link_or_file",
                schema: "courses",
                table: "lesson_materials");

            migrationBuilder.DropColumn(
                name: "video_content_type",
                schema: "courses",
                table: "lessons");

            migrationBuilder.DropColumn(
                name: "video_file_id",
                schema: "courses",
                table: "lessons");

            migrationBuilder.DropColumn(
                name: "video_size",
                schema: "courses",
                table: "lessons");

            migrationBuilder.DropColumn(
                name: "content_type",
                schema: "courses",
                table: "lesson_materials");

            migrationBuilder.DropColumn(
                name: "file_id",
                schema: "courses",
                table: "lesson_materials");

            migrationBuilder.DropColumn(
                name: "size",
                schema: "courses",
                table: "lesson_materials");

            migrationBuilder.AlterColumn<string>(
                name: "url",
                schema: "courses",
                table: "lesson_materials",
                type: "character varying(2048)",
                maxLength: 2048,
                nullable: false,
                defaultValue: "",
                oldClrType: typeof(string),
                oldType: "character varying(2048)",
                oldMaxLength: 2048,
                oldNullable: true);
        }
    }
}
