using AulaVirtual.Modules.Courses.Domain.Content;
using AulaVirtual.Modules.Courses.Domain.Courses;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AulaVirtual.Modules.Courses.Infrastructure.Persistence.Configurations;

internal sealed class CourseUnitConfiguration : IEntityTypeConfiguration<CourseUnit>
{
    public void Configure(EntityTypeBuilder<CourseUnit> builder)
    {
        builder.ToTable("course_units", "courses", table =>
            table.HasCheckConstraint("ck_course_units_position", "position >= 1"));
        builder.HasKey(unit => unit.Id).HasName("pk_course_units");
        builder.Property(unit => unit.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(unit => unit.CourseId).HasColumnName("course_id").IsRequired();
        builder.Property(unit => unit.Title).HasColumnName("title").HasMaxLength(CourseUnit.MaxTitleLength).IsRequired();
        builder.Property(unit => unit.Position).HasColumnName("position").IsRequired();
        builder.Property(unit => unit.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(unit => unit.UpdatedAt).HasColumnName("updated_at");
        builder.HasOne<Course>().WithMany().HasForeignKey(unit => unit.CourseId)
            .HasConstraintName("fk_course_units_courses_course_id").OnDelete(DeleteBehavior.Cascade);
        // A course's units, in order.
        builder.HasIndex(unit => new { unit.CourseId, unit.Position }).HasDatabaseName("ix_course_units_course_position");
    }
}

internal sealed class LessonConfiguration : IEntityTypeConfiguration<Lesson>
{
    public void Configure(EntityTypeBuilder<Lesson> builder)
    {
        builder.ToTable("lessons", "courses", table =>
        {
            table.HasCheckConstraint("ck_lessons_position", "position >= 1");
            table.HasCheckConstraint("ck_lessons_video_link_or_file", "video_url IS NULL OR video_file_id IS NULL");
        });
        builder.HasKey(lesson => lesson.Id).HasName("pk_lessons");
        builder.Property(lesson => lesson.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(lesson => lesson.CourseId).HasColumnName("course_id").IsRequired();
        builder.Property(lesson => lesson.UnitId).HasColumnName("unit_id").IsRequired();
        builder.Property(lesson => lesson.Title).HasColumnName("title").HasMaxLength(Lesson.MaxTitleLength).IsRequired();
        builder.Property(lesson => lesson.Summary).HasColumnName("summary").HasMaxLength(Lesson.MaxSummaryLength);
        builder.Property(lesson => lesson.VideoUrl).HasColumnName("video_url").HasMaxLength(2048);
        builder.Property(lesson => lesson.VideoFileId).HasColumnName("video_file_id");
        builder.Property(lesson => lesson.VideoContentType).HasColumnName("video_content_type").HasMaxLength(100);
        builder.Property(lesson => lesson.VideoSize).HasColumnName("video_size");
        builder.Property(lesson => lesson.Position).HasColumnName("position").IsRequired();
        builder.Property(lesson => lesson.IsPublished).HasColumnName("is_published").IsRequired();
        builder.Property(lesson => lesson.CreatedAt).HasColumnName("created_at").IsRequired();
        builder.Property(lesson => lesson.UpdatedAt).HasColumnName("updated_at");

        builder.HasOne<Course>().WithMany().HasForeignKey(lesson => lesson.CourseId)
            .HasConstraintName("fk_lessons_courses_course_id").OnDelete(DeleteBehavior.Cascade);
        // A unit with lessons cannot be deleted (the use case says so first; the database makes sure). NO ACTION,
        // not RESTRICT: it is checked at the end of the statement, so deleting a whole course (which cascades to
        // its units and its lessons at once) still works.
        builder.HasOne<CourseUnit>().WithMany().HasForeignKey(lesson => lesson.UnitId)
            .HasConstraintName("fk_lessons_course_units_unit_id").OnDelete(DeleteBehavior.NoAction);

        builder.HasMany(lesson => lesson.Materials).WithOne().HasForeignKey(material => material.LessonId)
            .HasConstraintName("fk_lesson_materials_lessons_lesson_id").OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(lesson => lesson.Materials).HasField("materials").UsePropertyAccessMode(PropertyAccessMode.Field);

        // The whole course in one indexed read (students: only published lessons).
        builder.HasIndex(lesson => new { lesson.CourseId, lesson.IsPublished }).HasDatabaseName("ix_lessons_course_published");
        // A unit's lessons, in order (reorder, next position, «unit has lessons»).
        builder.HasIndex(lesson => new { lesson.UnitId, lesson.Position }).HasDatabaseName("ix_lessons_unit_position");
    }
}

internal sealed class LessonMaterialConfiguration : IEntityTypeConfiguration<LessonMaterial>
{
    public void Configure(EntityTypeBuilder<LessonMaterial> builder)
    {
        builder.ToTable("lesson_materials", "courses");
        builder.HasKey(material => material.Id).HasName("pk_lesson_materials");
        builder.Property(material => material.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(material => material.LessonId).HasColumnName("lesson_id").IsRequired();
        builder.Property(material => material.Title).HasColumnName("title").HasMaxLength(LessonMaterial.MaxTitleLength).IsRequired();
        builder.Property(material => material.Url).HasColumnName("url").HasMaxLength(2048);
        builder.Property(material => material.FileId).HasColumnName("file_id");
        builder.Property(material => material.ContentType).HasColumnName("content_type").HasMaxLength(100);
        builder.Property(material => material.Size).HasColumnName("size");
        // A material is a link or an uploaded file, never both and never neither.
        builder.ToTable(table => table.HasCheckConstraint("ck_lesson_materials_link_or_file",
            "(url IS NOT NULL AND file_id IS NULL) OR (url IS NULL AND file_id IS NOT NULL AND content_type IS NOT NULL AND size > 0)"));
        builder.Property(material => material.Position).HasColumnName("position").IsRequired();
        builder.HasIndex(material => new { material.LessonId, material.Position }).HasDatabaseName("ix_lesson_materials_lesson_position");
    }
}
