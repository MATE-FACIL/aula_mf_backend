using AulaVirtual.Modules.Enrollments.Domain;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Enrollments.Infrastructure.Persistence;

public sealed class EnrollmentsDbContext(DbContextOptions<EnrollmentsDbContext> options)
    : DbContext(options)
{
    public DbSet<Enrollment> Enrollments => Set<Enrollment>();

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
    }
}
