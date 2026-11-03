using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AulaVirtual.Modules.Enrollments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialEnrollments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "enrollments");

            migrationBuilder.CreateTable(
                name: "enrollments",
                schema: "enrollments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    course_offering_id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    payment_note = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    created_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    activated_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    withdrawn_by_user_id = table.Column<Guid>(type: "uuid", nullable: true),
                    withdrawn_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_enrollments", x => x.id);
                    table.CheckConstraint("ck_enrollments_status", "status IN ('pending', 'active', 'withdrawn')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_enrollments_created_at",
                schema: "enrollments",
                table: "enrollments",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_enrollments_offering_status",
                schema: "enrollments",
                table: "enrollments",
                columns: new[] { "course_offering_id", "status" });

            migrationBuilder.CreateIndex(
                name: "ux_enrollments_student_offering_live",
                schema: "enrollments",
                table: "enrollments",
                columns: new[] { "student_user_id", "course_offering_id" },
                unique: true,
                filter: "status <> 'withdrawn'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "enrollments",
                schema: "enrollments");
        }
    }
}
