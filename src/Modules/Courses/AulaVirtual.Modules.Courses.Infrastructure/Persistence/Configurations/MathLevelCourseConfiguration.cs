using AulaVirtual.Modules.Courses.Domain.Courses;
using AulaVirtual.Modules.Courses.Domain.Levels;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AulaVirtual.Modules.Courses.Infrastructure.Persistence.Configurations;

internal sealed class MathLevelCourseConfiguration : IEntityTypeConfiguration<MathLevelCourse>
{
    public void Configure(EntityTypeBuilder<MathLevelCourse> builder)
    {
        builder.ToTable("math_level_courses", "courses", table =>
        {
            table.HasCheckConstraint("ck_math_level_courses_stage", "stage IN ('primary', 'secondary')");
            table.HasCheckConstraint("ck_math_level_courses_level", "level IN ('prebasic', 'basic', 'intermediate', 'advanced')");
        });
        // One course per level: the key is the level itself.
        builder.HasKey(link => new { link.Stage, link.Level }).HasName("pk_math_level_courses");
        builder.Property(link => link.Stage).HasColumnName("stage").HasMaxLength(16)
            .HasConversion(value => MathLevels.StageCode(value), value => value == "primary" ? EducationStage.Primary : EducationStage.Secondary);
        builder.Property(link => link.Level).HasColumnName("level").HasMaxLength(16)
            .HasConversion(value => MathLevels.LevelCode(value), value => ParseLevel(value));
        builder.Property(link => link.CourseId).HasColumnName("course_id").IsRequired();
        builder.Property(link => link.CreatedAt).HasColumnName("created_at").IsRequired();
        // A course is at most one level; deleting the course frees the level.
        builder.HasIndex(link => link.CourseId).HasDatabaseName("ux_math_level_courses_course").IsUnique();
        builder.HasOne<Course>().WithMany().HasForeignKey(link => link.CourseId)
            .HasConstraintName("fk_math_level_courses_courses_course_id").OnDelete(DeleteBehavior.Cascade);
    }

    private static MathLevel ParseLevel(string value) => value switch
    {
        "prebasic" => MathLevel.PreBasic,
        "basic" => MathLevel.Basic,
        "intermediate" => MathLevel.Intermediate,
        _ => MathLevel.Advanced
    };
}
