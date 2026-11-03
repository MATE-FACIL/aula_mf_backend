using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AulaVirtual.Modules.Courses.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddCourseSessionDuration : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "session_duration_minutes",
                schema: "courses",
                table: "courses",
                type: "integer",
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "ck_courses_session_duration_minutes",
                schema: "courses",
                table: "courses",
                sql: "session_duration_minutes IS NULL OR session_duration_minutes BETWEEN 30 AND 240");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "ck_courses_session_duration_minutes",
                schema: "courses",
                table: "courses");

            migrationBuilder.DropColumn(
                name: "session_duration_minutes",
                schema: "courses",
                table: "courses");
        }
    }
}
