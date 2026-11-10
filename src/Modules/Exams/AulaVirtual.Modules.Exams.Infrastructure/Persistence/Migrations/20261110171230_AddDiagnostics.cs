using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AulaVirtual.Modules.Exams.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddDiagnostics : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "diagnostic_attempts",
                schema: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    question_ids = table.Column<Guid[]>(type: "uuid[]", nullable: false),
                    status = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    suggested_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    confirmed_level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    confirmed_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    confirmed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diagnostic_attempts", x => x.id);
                    table.CheckConstraint("ck_diagnostic_attempts_completed", "(status = 'in_progress' AND completed_at IS NULL AND suggested_level IS NULL) OR (status = 'completed' AND completed_at IS NOT NULL AND suggested_level IS NOT NULL)");
                    table.CheckConstraint("ck_diagnostic_attempts_confirmed", "(confirmed_level IS NULL) = (confirmed_by_user_id IS NULL) AND (confirmed_level IS NULL) = (confirmed_at IS NULL)");
                    table.CheckConstraint("ck_diagnostic_attempts_stage", "stage IN ('primary', 'secondary')");
                    table.CheckConstraint("ck_diagnostic_attempts_status", "status IN ('in_progress', 'completed')");
                });

            migrationBuilder.CreateTable(
                name: "diagnostic_questions",
                schema: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    stage = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    topic = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
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
                    table.PrimaryKey("pk_diagnostic_questions", x => x.id);
                    table.CheckConstraint("ck_diagnostic_questions_answer", "(kind = 'multiple_choice' AND correct_option IS NOT NULL) OR (kind = 'numeric' AND correct_number IS NOT NULL)");
                    table.CheckConstraint("ck_diagnostic_questions_kind", "kind IN ('multiple_choice', 'numeric')");
                    table.CheckConstraint("ck_diagnostic_questions_level", "level IN ('prebasic', 'basic', 'intermediate', 'advanced')");
                    table.CheckConstraint("ck_diagnostic_questions_position", "position >= 1");
                    table.CheckConstraint("ck_diagnostic_questions_stage", "stage IN ('primary', 'secondary')");
                });

            migrationBuilder.CreateTable(
                name: "diagnostic_answers",
                schema: "exams",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    attempt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    question_id = table.Column<Guid>(type: "uuid", nullable: false),
                    topic = table.Column<string>(type: "character varying(80)", maxLength: 80, nullable: false),
                    level = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    answer = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    is_correct = table.Column<bool>(type: "boolean", nullable: false),
                    answered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_diagnostic_answers", x => x.id);
                    table.CheckConstraint("ck_diagnostic_answers_skipped", "answer IS NOT NULL OR NOT is_correct");
                    table.ForeignKey(
                        name: "fk_diagnostic_answers_diagnostic_attempts_attempt_id",
                        column: x => x.attempt_id,
                        principalSchema: "exams",
                        principalTable: "diagnostic_attempts",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_diagnostic_answers_diagnostic_questions_question_id",
                        column: x => x.question_id,
                        principalSchema: "exams",
                        principalTable: "diagnostic_questions",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_diagnostic_answers_question",
                schema: "exams",
                table: "diagnostic_answers",
                column: "question_id");

            migrationBuilder.CreateIndex(
                name: "ux_diagnostic_answers_attempt_question",
                schema: "exams",
                table: "diagnostic_answers",
                columns: new[] { "attempt_id", "question_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_diagnostic_attempts_status_started_at",
                schema: "exams",
                table: "diagnostic_attempts",
                columns: new[] { "status", "started_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ux_diagnostic_attempts_student_stage",
                schema: "exams",
                table: "diagnostic_attempts",
                columns: new[] { "student_user_id", "stage" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_diagnostic_questions_stage_active_position",
                schema: "exams",
                table: "diagnostic_questions",
                columns: new[] { "stage", "is_archived", "position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "diagnostic_answers",
                schema: "exams");

            migrationBuilder.DropTable(
                name: "diagnostic_attempts",
                schema: "exams");

            migrationBuilder.DropTable(
                name: "diagnostic_questions",
                schema: "exams");
        }
    }
}
