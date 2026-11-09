using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AulaVirtual.Modules.Exams.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialPractice : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "exams");

            migrationBuilder.CreateTable(
                name: "exercises",
                schema: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    lesson_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    prompt = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    options = table.Column<string[]>(type: "text[]", nullable: false),
                    correct_option = table.Column<int>(type: "integer", nullable: true),
                    correct_number = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    tolerance = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    explanation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    is_archived = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exercises", x => x.id);
                    table.CheckConstraint("ck_exercises_answer", "(kind = 'multiple_choice' AND correct_option IS NOT NULL) OR (kind = 'numeric' AND correct_number IS NOT NULL)");
                    table.CheckConstraint("ck_exercises_kind", "kind IN ('multiple_choice', 'numeric')");
                    table.CheckConstraint("ck_exercises_position", "position >= 1");
                });

            migrationBuilder.CreateTable(
                name: "exercise_attempts",
                schema: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    exercise_id = table.Column<Guid>(type: "uuid", nullable: false),
                    lesson_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    answer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    is_correct = table.Column<bool>(type: "boolean", nullable: false),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_exercise_attempts", x => x.id);
                    table.ForeignKey(
                        name: "fk_exercise_attempts_exercises_exercise_id",
                        column: x => x.exercise_id,
                        principalSchema: "exams",
                        principalTable: "exercises",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_exercise_attempts_exercise",
                schema: "exams",
                table: "exercise_attempts",
                column: "exercise_id");

            migrationBuilder.CreateIndex(
                name: "ix_exercise_attempts_student_answered_at",
                schema: "exams",
                table: "exercise_attempts",
                columns: new[] { "student_user_id", "answered_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_exercise_attempts_student_exercise",
                schema: "exams",
                table: "exercise_attempts",
                columns: new[] { "student_user_id", "exercise_id" });

            migrationBuilder.CreateIndex(
                name: "ix_exercise_attempts_student_lesson",
                schema: "exams",
                table: "exercise_attempts",
                columns: new[] { "student_user_id", "lesson_id", "is_correct" });

            migrationBuilder.CreateIndex(
                name: "ix_exercises_lesson_active_position",
                schema: "exams",
                table: "exercises",
                columns: new[] { "lesson_id", "is_archived", "position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "exercise_attempts",
                schema: "exams");

            migrationBuilder.DropTable(
                name: "exercises",
                schema: "exams");
        }
    }
}
