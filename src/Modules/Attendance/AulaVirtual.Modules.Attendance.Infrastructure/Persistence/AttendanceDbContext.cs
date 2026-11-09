using AulaVirtual.Modules.Attendance.Domain;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Attendance.Infrastructure.Persistence;

public sealed class AttendanceDbContext(DbContextOptions<AttendanceDbContext> options) : DbContext(options)
{
    public DbSet<AttendanceRecord> Records => Set<AttendanceRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(AttendanceDbOptions.Schema);

        var record = modelBuilder.Entity<AttendanceRecord>();
        record.ToTable("attendance_records", AttendanceDbOptions.Schema, table =>
            table.HasCheckConstraint("ck_attendance_records_status", "status IN ('present', 'late', 'absent')"));
        record.HasKey(item => item.Id).HasName("pk_attendance_records");
        record.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        record.Property(item => item.SessionId).HasColumnName("session_id").IsRequired();
        record.Property(item => item.CourseOfferingId).HasColumnName("course_offering_id").IsRequired();
        record.Property(item => item.SessionStartsAt).HasColumnName("session_starts_at").IsRequired();
        record.Property(item => item.StudentUserId).HasColumnName("student_user_id").IsRequired();
        record.Property(item => item.Status).HasColumnName("status").HasMaxLength(16)
            .HasConversion(value => AttendanceStatusCodes.ToCode(value), value => Parse(value))
            .IsRequired();
        record.Property(item => item.Note).HasColumnName("note").HasMaxLength(AttendanceRecord.MaxNoteLength);
        record.Property(item => item.RecordedByUserId).HasColumnName("recorded_by_user_id").IsRequired();
        record.Property(item => item.RecordedAt).HasColumnName("recorded_at").IsRequired();
        record.Property(item => item.UpdatedByUserId).HasColumnName("updated_by_user_id");
        record.Property(item => item.UpdatedAt).HasColumnName("updated_at");

        // One record per class and student; also serves «the roster of a class».
        record.HasIndex(item => new { item.SessionId, item.StudentUserId })
            .HasDatabaseName("ux_attendance_records_session_student")
            .IsUnique();
        // A student's history, newest first, and the per-student totals.
        record.HasIndex(item => new { item.StudentUserId, item.SessionStartsAt })
            .HasDatabaseName("ix_attendance_records_student_starts_at")
            .IsDescending(false, true);
    }

    private static AttendanceStatus Parse(string value) =>
        AttendanceStatusCodes.TryParse(value, out var status)
            ? status
            : throw new InvalidOperationException($"Unknown attendance status «{value}».");
}
