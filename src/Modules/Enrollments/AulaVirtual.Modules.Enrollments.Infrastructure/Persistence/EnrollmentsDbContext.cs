using AulaVirtual.Modules.Enrollments.Domain;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Enrollments.Infrastructure.Persistence;

public sealed class EnrollmentsDbContext(DbContextOptions<EnrollmentsDbContext> options)
    : DbContext(options)
{
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();
    public DbSet<EnrollmentTransfer> Transfers => Set<EnrollmentTransfer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("enrollments");

        var enrollment = modelBuilder.Entity<Enrollment>();
        enrollment.ToTable("enrollments", "enrollments");
        enrollment.HasKey(item => item.Id).HasName("pk_enrollments");
        enrollment.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        enrollment.Property(item => item.StudentUserId).HasColumnName("student_user_id").IsRequired();
        enrollment.Property(item => item.CourseOfferingId).HasColumnName("course_offering_id").IsRequired();
        enrollment.Property(item => item.Status).HasColumnName("status").HasMaxLength(16)
            .HasConversion(
                value => value == EnrollmentStatus.Pending ? "pending"
                    : value == EnrollmentStatus.Active ? "active" : "withdrawn",
                value => value == "pending" ? EnrollmentStatus.Pending
                    : value == "active" ? EnrollmentStatus.Active : EnrollmentStatus.Withdrawn)
            .IsRequired();
        enrollment.Property(item => item.PaymentNote).HasColumnName("payment_note")
            .HasMaxLength(Enrollment.MaxPaymentNoteLength);
        enrollment.Property(item => item.CreatedByUserId).HasColumnName("created_by_user_id").IsRequired();
        enrollment.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        enrollment.Property(item => item.ActivatedByUserId).HasColumnName("activated_by_user_id");
        enrollment.Property(item => item.ActivatedAt).HasColumnName("activated_at");
        enrollment.Property(item => item.WithdrawnByUserId).HasColumnName("withdrawn_by_user_id");
        enrollment.Property(item => item.WithdrawnAt).HasColumnName("withdrawn_at");
        enrollment.Property(item => item.UpdatedAt).HasColumnName("updated_at");
        enrollment.Ignore(item => item.HoldsSeat);

        // One live seat per student and edition; withdrawn rows stay as history.
        enrollment.HasIndex(item => new { item.StudentUserId, item.CourseOfferingId })
            .HasDatabaseName("ux_enrollments_student_offering_live")
            .IsUnique()
            .HasFilter("status <> 'withdrawn'");
        // Rosters and seat counts per edition.
        enrollment.HasIndex(item => new { item.CourseOfferingId, item.Status })
            .HasDatabaseName("ix_enrollments_offering_status");
        // Admin list: newest first.
        enrollment.HasIndex(item => item.CreatedAt).HasDatabaseName("ix_enrollments_created_at");

        enrollment.ToTable(table => table.HasCheckConstraint("ck_enrollments_status",
            "status IN ('pending', 'active', 'withdrawn')"));

        // Changes of group (SCRUM-45): who moved the student, when, between which groups and why.
        var transfer = modelBuilder.Entity<EnrollmentTransfer>();
        transfer.ToTable("enrollment_transfers", "enrollments", table =>
            table.HasCheckConstraint("ck_enrollment_transfers_groups", "from_offering_id <> to_offering_id"));
        transfer.HasKey(item => item.Id).HasName("pk_enrollment_transfers");
        transfer.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        transfer.Property(item => item.StudentUserId).HasColumnName("student_user_id").IsRequired();
        transfer.Property(item => item.FromEnrollmentId).HasColumnName("from_enrollment_id").IsRequired();
        transfer.Property(item => item.ToEnrollmentId).HasColumnName("to_enrollment_id").IsRequired();
        transfer.Property(item => item.FromOfferingId).HasColumnName("from_offering_id").IsRequired();
        transfer.Property(item => item.ToOfferingId).HasColumnName("to_offering_id").IsRequired();
        transfer.Property(item => item.Reason).HasColumnName("reason").HasMaxLength(EnrollmentTransfer.MaxReasonLength);
        transfer.Property(item => item.TransferredByUserId).HasColumnName("transferred_by_user_id").IsRequired();
        transfer.Property(item => item.TransferredAt).HasColumnName("transferred_at").IsRequired();
        transfer.HasOne<Enrollment>().WithMany().HasForeignKey(item => item.FromEnrollmentId)
            .HasConstraintName("fk_enrollment_transfers_enrollments_from_enrollment_id").OnDelete(DeleteBehavior.Restrict);
        transfer.HasOne<Enrollment>().WithMany().HasForeignKey(item => item.ToEnrollmentId)
            .HasConstraintName("fk_enrollment_transfers_enrollments_to_enrollment_id").OnDelete(DeleteBehavior.Restrict);
        // A student's changes, newest first; the newest overall; and the moves out of or into a group.
        transfer.HasIndex(item => new { item.StudentUserId, item.TransferredAt })
            .HasDatabaseName("ix_enrollment_transfers_student_transferred_at").IsDescending(false, true);
        transfer.HasIndex(item => item.TransferredAt).HasDatabaseName("ix_enrollment_transfers_transferred_at").IsDescending();
        transfer.HasIndex(item => item.FromOfferingId).HasDatabaseName("ix_enrollment_transfers_from_offering");
        transfer.HasIndex(item => item.ToOfferingId).HasDatabaseName("ix_enrollment_transfers_to_offering");
        // A seat is moved at most once (it is withdrawn when it moves) and a seat comes from at most one move.
        transfer.HasIndex(item => item.FromEnrollmentId).IsUnique().HasDatabaseName("ux_enrollment_transfers_from_enrollment");
        transfer.HasIndex(item => item.ToEnrollmentId).IsUnique().HasDatabaseName("ux_enrollment_transfers_to_enrollment");
    }
}
