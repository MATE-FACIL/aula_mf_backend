using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AulaVirtual.Modules.Exams.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEvaluations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "evaluations",
                schema: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    offering_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_id = table.Column<Guid>(type: "uuid", nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    title = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: false),
                    opens_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    closes_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    duration_minutes = table.Column<int>(type: "integer", nullable: false),
                    is_published = table.Column<bool>(type: "boolean", nullable: false),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evaluations", x => x.id);
                    table.CheckConstraint("ck_evaluations_duration", "duration_minutes BETWEEN 5 AND 180");
                    table.CheckConstraint("ck_evaluations_kind", "kind IN ('weekly', 'midterm', 'final')");
                    table.CheckConstraint("ck_evaluations_window", "closes_at > opens_at");
                });

            migrationBuilder.CreateTable(
                name: "evaluation_attempts",
                schema: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    submitted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    correct_count = table.Column<int>(type: "integer", nullable: false),
                    question_count = table.Column<int>(type: "integer", nullable: false),
                    score = table.Column<decimal>(type: "numeric(4,1)", precision: 4, scale: 1, nullable: true),
                    is_late = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evaluation_attempts", x => x.id);
                    table.CheckConstraint("ck_evaluation_attempts_submitted", "(submitted_at IS NULL AND score IS NULL) OR (submitted_at IS NOT NULL AND score IS NOT NULL AND score BETWEEN 0 AND 20)");
                    table.ForeignKey(
                        name: "fk_evaluation_attempts_evaluations_evaluation_id",
                        column: x => x.evaluation_id,
                        principalSchema: "exams",
                        principalTable: "evaluations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_questions",
                schema: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    evaluation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    position = table.Column<int>(type: "integer", nullable: false),
                    competency = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    kind = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    prompt = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    options = table.Column<string[]>(type: "text[]", nullable: false),
                    correct_option = table.Column<int>(type: "integer", nullable: true),
                    correct_number = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: true),
                    tolerance = table.Column<decimal>(type: "numeric(18,6)", precision: 18, scale: 6, nullable: false),
                    explanation = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evaluation_questions", x => x.id);
                    table.CheckConstraint("ck_evaluation_questions_answer", "(kind = 'multiple_choice' AND correct_option IS NOT NULL) OR (kind = 'numeric' AND correct_number IS NOT NULL)");
                    table.CheckConstraint("ck_evaluation_questions_kind", "kind IN ('multiple_choice', 'numeric')");
                    table.CheckConstraint("ck_evaluation_questions_position", "position >= 1");
                    table.ForeignKey(
                        name: "fk_evaluation_questions_evaluations_evaluation_id",
                        column: x => x.evaluation_id,
                        principalSchema: "exams",
                        principalTable: "evaluations",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "evaluation_answers",
                schema: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    competency = table.Column<string>(type: "character varying(30)", maxLength: 30, nullable: false),
                    answer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_correct = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_evaluation_answers", x => x.id);
                    table.ForeignKey(
                        name: "fk_evaluation_answers_evaluation_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalSchema: "exams",
                        principalTable: "evaluation_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_evaluation_answers_evaluation_questions_question_id",
                        column: x => x.question_id,
                        principalSchema: "exams",
                        principalTable: "evaluation_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_evaluation_answers_question",
                schema: "exams",
                table: "evaluation_answers",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ux_evaluation_answers_attempt_question",
                schema: "exams",
                table: "evaluation_answers",
                columns: new[] { "attempt_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_evaluation_attempts_student",
                schema: "exams",
                table: "evaluation_attempts",
                column: "student_user_id");

            migrationBuilder.CreateIndex(
                name: "ux_evaluation_attempts_evaluation_student",
                schema: "exams",
                table: "evaluation_attempts",
                columns: new[] { "evaluation_id", "student_user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_evaluation_questions_evaluation_position",
                schema: "exams",
                table: "evaluation_questions",
                columns: new[] { "evaluation_id", "position" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_evaluations_offering_opens_at",
                schema: "exams",
                table: "evaluations",
                columns: new[] { "offering_id", "opens_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "evaluation_answers",
                schema: "exams");

            migrationBuilder.DropTable(
                name: "evaluation_attempts",
                schema: "exams");

            migrationBuilder.DropTable(
                name: "evaluation_questions",
                schema: "exams");

            migrationBuilder.DropTable(
                name: "evaluations",
                schema: "exams");
        }
    }
}
