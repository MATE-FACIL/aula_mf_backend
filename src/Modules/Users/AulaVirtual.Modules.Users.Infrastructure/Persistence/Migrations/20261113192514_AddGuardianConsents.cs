using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AulaVirtual.Modules.Users.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddGuardianConsents : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "guardian_consents",
                schema: "users",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    guardian_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    terms_version = table.Column<string>(type: "character varying(60)", maxLength: 60, nullable: false),
                    accepted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_guardian_consents", x => x.id);
                    table.CheckConstraint("ck_guardian_consents_distinct_users", "guardian_user_id <> student_user_id");
                    table.ForeignKey(
                        name: "fk_guardian_consents_users_guardian_user_id",
                        column: x => x.guardian_user_id,
                        principalSchema: "users",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_guardian_consents_users_student_user_id",
                        column: x => x.student_user_id,
                        principalSchema: "users",
                        principalTable: "users",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_guardian_consents_student_version",
                schema: "users",
                table: "guardian_consents",
                columns: new[] { "student_user_id", "terms_version" });

            migrationBuilder.CreateIndex(
                name: "ux_guardian_consents_guardian_student_version",
                schema: "users",
                table: "guardian_consents",
                columns: new[] { "guardian_user_id", "student_user_id", "terms_version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "guardian_consents",
                schema: "users");
        }
    }
}
