using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AulaVirtual.Modules.Enrollments.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddEnrollmentTransfers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "enrollment_transfers",
                schema: "enrollments",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    student_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                    from_offering_id = table.Column<Guid>(type: "uuid", nullable: false),
                    to_offering_id = table.Column<Guid>(type: "uuid", nullable: false),
                    reason = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    transferred_by_user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    transferred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_enrollment_transfers", x => x.id);
                    table.CheckConstraint("ck_enrollment_transfers_groups", "from_offering_id <> to_offering_id");
                    table.ForeignKey(
                        name: "fk_enrollment_transfers_enrollments_from_enrollment_id",
                        column: x => x.from_enrollment_id,
                        principalSchema: "enrollments",
                        principalTable: "enrollments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "fk_enrollment_transfers_enrollments_to_enrollment_id",
                        column: x => x.to_enrollment_id,
                        principalSchema: "enrollments",
                        principalTable: "enrollments",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "ix_enrollment_transfers_from_offering",
                schema: "enrollments",
                table: "enrollment_transfers",
                column: "from_offering_id");

            migrationBuilder.CreateIndex(
                name: "ix_enrollment_transfers_student_transferred_at",
                schema: "enrollments",
                table: "enrollment_transfers",
                columns: new[] { "student_user_id", "transferred_at" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "ix_enrollment_transfers_to_offering",
                schema: "enrollments",
                table: "enrollment_transfers",
                column: "to_offering_id");

            migrationBuilder.CreateIndex(
                name: "ix_enrollment_transfers_transferred_at",
                schema: "enrollments",
                table: "enrollment_transfers",
                column: "transferred_at",
                descending: new bool[0]);

            migrationBuilder.CreateIndex(
                name: "ux_enrollment_transfers_from_enrollment",
                schema: "enrollments",
                table: "enrollment_transfers",
                column: "from_enrollment_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ux_enrollment_transfers_to_enrollment",
                schema: "enrollments",
                table: "enrollment_transfers",
                column: "to_enrollment_id",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "enrollment_transfers",
                schema: "enrollments");
        }
    }
}
