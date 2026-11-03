using AulaVirtual.Modules.Courses.Domain.Offerings;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AulaVirtual.Modules.Courses.Infrastructure.Persistence.Configurations;

internal sealed class OfferingSessionConfiguration : IEntityTypeConfiguration<OfferingSession>
{
    public void Configure(EntityTypeBuilder<OfferingSession> builder)
    {
        builder.ToTable("offering_sessions", "courses", table =>
            table.HasCheckConstraint("ck_offering_sessions_time", "starts_at < ends_at"));
        builder.HasKey(session => session.Id).HasName("pk_offering_sessions");
        builder.Property(session => session.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(session => session.CourseOfferingId).HasColumnName("course_offering_id").IsRequired();
        builder.Property(session => session.StartsAt).HasColumnName("starts_at").IsRequired();
        builder.Property(session => session.EndsAt).HasColumnName("ends_at").IsRequired();
        builder.Property(session => session.VirtualLink).HasColumnName("virtual_link").HasMaxLength(2048);
        // Serves both «sessions of an edition» and «sessions of these editions between two dates, in order»
        // (student schedule), so a separate single-column index is not needed.
        builder.HasIndex(session => new { session.CourseOfferingId, session.StartsAt })
            .HasDatabaseName("ix_offering_sessions_offering_starts_at");
    }
}
