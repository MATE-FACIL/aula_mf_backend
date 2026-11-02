using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AulaVirtual.Modules.Users.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuardianLinks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:unaccent", ",,");

            migrationBuilder.CreateTable(
                name: "guardian_links",
                schema: "users",
                columns: table => new
                {
                    guardian_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guardian_links", x => new { x.guardian_user_id, x.student_user_id });
                    table.CheckConstraint("ck_guardian_links_distinct_users", "guardian_user_id <> student_user_id");
                    table.ForeignKey(
                        name: "fk_guardian_links_users_guardian_user_id",
                        column: x => x.guardian_user_id,
                        principalSchema: "users",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_guardian_links_users_student_user_id",
                        column: x => x.student_user_id,
                        principalSchema: "users",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_users_created_at",
                schema: "users",
                table: "users",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_guardian_links_student_user_id",
                schema: "users",
                table: "guardian_links",
                column: "student_user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guardian_links",
                schema: "users");

            migrationBuilder.DropIndex(
                name: "ix_users_created_at",
                schema: "users",
                table: "users");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:unaccent", ",,");
        }
    }
}
