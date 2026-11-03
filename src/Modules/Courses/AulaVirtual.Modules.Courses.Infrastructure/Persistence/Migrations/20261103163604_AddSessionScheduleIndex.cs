using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AulaVirtual.Modules.Courses.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddSessionScheduleIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_offering_sessions_course_offering_id",
                schema: "courses",
                table: "offering_sessions");

            migrationBuilder.CreateIndex(
                name: "ix_offering_sessions_offering_starts_at",
                schema: "courses",
                table: "offering_sessions",
                columns: new[] { "course_offering_id", "starts_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_offering_sessions_offering_starts_at",
                schema: "courses",
                table: "offering_sessions");

            migrationBuilder.CreateIndex(
                name: "ix_offering_sessions_course_offering_id",
                schema: "courses",
                table: "offering_sessions",
                column: "course_offering_id");
        }
    }
}
