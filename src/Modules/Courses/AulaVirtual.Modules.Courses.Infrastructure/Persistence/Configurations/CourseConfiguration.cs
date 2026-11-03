using AulaVirtual.Modules.Courses.Domain.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AulaVirtual.Modules.Courses.Infrastructure.Persistence.Configurations;

internal sealed class CourseConfiguration : IEntityTypeConfiguration<Course>
{
    public void Configure(EntityTypeBuilder<Course> builder)
    {
        builder.ToTable("courses", "courses", table =>
        {
            table.HasCheckConstraint("ck_courses_published_category", "NOT is_published OR category_id IS NOT NULL");
            table.HasCheckConstraint("ck_courses_session_duration_minutes",
                "session_duration_minutes IS NULL OR session_duration_minutes BETWEEN 30 AND 240");
        });

        builder.HasKey(course => course.Id).HasName("pk_courses");
        builder.Property(course => course.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(course => course.Title).HasColumnName("title").HasMaxLength(200).IsRequired();
        builder.Property(course => course.CategoryId).HasColumnName("category_id");
        builder.Property(course => course.IsPublished)
            .HasColumnName("is_published")
            .HasDefaultValue(false)
            .IsRequired();
        builder.Property(course => course.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(course => course.UpdatedAt).HasColumnName("updated_at");
        builder.Property(course => course.ShortDescription).HasColumnName("short_description").HasColumnType("text");
        builder.Property(course => course.FullDescription).HasColumnName("full_description").HasColumnType("text");
        builder.Property(course => course.Objectives).HasColumnName("objectives").HasColumnType("text");
        builder.Property(course => course.Syllabus).HasColumnName("syllabus").HasColumnType("text");
        builder.Property(course => course.TargetAudience).HasColumnName("target_audience").HasColumnType("text");
        builder.Property(course => course.Prerequisites).HasColumnName("prerequisites").HasColumnType("text");
        builder.Property(course => course.SessionDurationMinutes).HasColumnName("session_duration_minutes");
        builder.Property(course => course.CoverObjectKey).HasColumnName("cover_object_key").HasMaxLength(200);
        builder.Property(course => course.CoverContentType).HasColumnName("cover_content_type").HasMaxLength(100);
        builder.Property(course => course.CoverSize).HasColumnName("cover_size");
        builder.Property(course => course.CoverOriginalFileName).HasColumnName("cover_original_file_name").HasMaxLength(255);

        builder.HasOne<CourseCategory>()
            .WithMany()
            .HasForeignKey(course => course.CategoryId)
            .OnDelete(DeleteBehavior.Restrict)
            .HasConstraintName("fk_courses_category_id");

        builder.HasIndex(course => course.CategoryId)
            .HasDatabaseName("ix_courses_category_id");
    }
}
