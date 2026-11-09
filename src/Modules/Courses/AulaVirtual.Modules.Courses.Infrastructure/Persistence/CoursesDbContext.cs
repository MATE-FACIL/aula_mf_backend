using AulaVirtual.Modules.Courses.Domain.Content;
using AulaVirtual.Modules.Courses.Domain.Courses;
using AulaVirtual.Modules.Courses.Domain.Instructors;
using AulaVirtual.Modules.Courses.Domain.Offerings;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Courses.Infrastructure.Persistence;

public sealed class CoursesDbContext(DbContextOptions<CoursesDbContext> options)
    : DbContext(options)
{
    public DbSet<Course> Courses => Set<Course>();
    public DbSet<CourseCategory> Categories => Set<CourseCategory>();
    public DbSet<CourseOffering> CourseOfferings => Set<CourseOffering>();
    public DbSet<OfferingSession> OfferingSessions => Set<OfferingSession>();
    public DbSet<Instructor> Instructors => Set<Instructor>();
    public DbSet<CourseOfferingInstructor> CourseOfferingInstructors => Set<CourseOfferingInstructor>();
    public DbSet<SpecificPromotion> SpecificPromotions => Set<SpecificPromotion>();
    public DbSet<CourseUnit> CourseUnits => Set<CourseUnit>();
    public DbSet<Lesson> Lessons => Set<Lesson>();
    public DbSet<LessonMaterial> LessonMaterials => Set<LessonMaterial>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("courses");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CoursesDbContext).Assembly);
    }
}
