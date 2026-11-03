using AulaVirtual.Modules.Courses.Domain.Courses;
using FluentValidation;

namespace AulaVirtual.Modules.Courses.Application.Courses;

public sealed class CourseUseCases(ICourseRepository repository,
    IValidator<SaveCourseCommand> validator, TimeProvider timeProvider)
{
    public async Task<CourseDetails> CreateCourseAsync(SaveCourseCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        await RequireCategoryAsync(command.CategoryId, cancellationToken);
        var course = Course.Create(Guid.NewGuid(), command.Title, command.CategoryId, timeProvider.GetUtcNow());
        ApplyContent(course, command);
        repository.Add(course);
        await repository.SaveChangesAsync(cancellationToken);
        return CourseDetails.From(course);
    }

    public async Task<CourseDetails?> UpdateCourseAsync(Guid id, SaveCourseCommand command,
        CancellationToken cancellationToken)
    {
        RequireId(id);
        await validator.ValidateAndThrowAsync(command, cancellationToken);
        var course = await repository.GetAsync(id, cancellationToken);
        if (course is null) return null;
        await RequireCategoryAsync(command.CategoryId, cancellationToken);
        ApplyContent(course, command);
        await repository.SaveChangesAsync(cancellationToken);
        return CourseDetails.From(course);
    }

    public async Task<CourseDetails?> GetAdminCourseByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireId(id);
        var course = await repository.GetAsync(id, cancellationToken);
        return course is null ? null : CourseDetails.From(course);
    }

    public async Task<IReadOnlyList<CourseDetails>> ListAdminCoursesAsync(CancellationToken cancellationToken) =>
        (await repository.ListAsync(cancellationToken)).Select(CourseDetails.From).ToArray();

    public async Task<IReadOnlyList<CourseCategoryDetails>> ListCourseCategoriesAsync(CancellationToken cancellationToken) =>
        (await repository.ListCategoriesAsync(cancellationToken)).Select(CourseCategoryDetails.From).ToArray();

    public async Task<CourseCategoryDetails?> GetCourseCategoryAsync(Guid id, CancellationToken cancellationToken)
    {
        RequireCategoryId(id);
        var category = await repository.GetCategoryAsync(id, cancellationToken);
        return category is null ? null : CourseCategoryDetails.From(category);
    }

    public async Task<CourseCategoryDetails> CreateCourseCategoryAsync(string name, CancellationToken cancellationToken)
    {
        var category = CourseCategory.Create(Guid.NewGuid(), name, timeProvider.GetUtcNow());
        repository.AddCategory(category);
        await repository.SaveChangesAsync(cancellationToken);
        return CourseCategoryDetails.From(category);
    }

    public async Task<CourseCategoryDetails?> UpdateCourseCategoryAsync(Guid id, string name,
        CancellationToken cancellationToken)
    {
        RequireCategoryId(id);
        var category = await repository.GetCategoryAsync(id, cancellationToken);
        if (category is null) return null;
        category.Rename(name, timeProvider.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
        return CourseCategoryDetails.From(category);
    }

    public async Task<CourseCategoryDeletion> DeleteCourseCategoryAsync(Guid id,
        CancellationToken cancellationToken)
    {
        RequireCategoryId(id);
        var category = await repository.GetCategoryAsync(id, cancellationToken);
        if (category is null) return CourseCategoryDeletion.NotFound;
        if (await repository.CategoryHasCoursesAsync(id, cancellationToken))
            return CourseCategoryDeletion.InUse;
        try
        {
            return await repository.DeleteCategoryAsync(category, cancellationToken)
                ? CourseCategoryDeletion.Deleted
                : CourseCategoryDeletion.NotFound;
        }
        catch (CourseCategoryInUseException)
        {
            return CourseCategoryDeletion.InUse;
        }
    }

    public async Task<CourseDetails?> SetPublicationAsync(Guid id, bool isPublished,
        CancellationToken cancellationToken)
    {
        RequireId(id);
        var course = await repository.GetAsync(id, cancellationToken);
        if (course is null) return null;
        if (isPublished)
        {
            if (course.CategoryId is not { } categoryId ||
                !await repository.CategoryExistsAsync(categoryId, cancellationToken))
                throw new InvalidOperationException("A course requires an existing category before publication.");
            course.Publish(timeProvider.GetUtcNow());
        }
        else course.Unpublish(timeProvider.GetUtcNow());
        await repository.SaveChangesAsync(cancellationToken);
        return CourseDetails.From(course);
    }

    private async Task RequireCategoryAsync(Guid? id, CancellationToken cancellationToken)
    {
        if (id is { } categoryId && !await repository.CategoryExistsAsync(categoryId, cancellationToken))
            throw new ArgumentException("Category does not exist.", nameof(id));
    }

    private void ApplyContent(Course course, SaveCourseCommand command)
    {
        var now = timeProvider.GetUtcNow();
        course.Update(command.Title, command.CategoryId, command.ShortDescription,
            command.FullDescription, command.Objectives, command.Syllabus,
            command.TargetAudience, command.Prerequisites, now);
        course.SetSessionDuration(command.SessionDurationMinutes, now);
    }

    private static void RequireId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Course identifier cannot be empty.", nameof(id));
    }

    private static void RequireCategoryId(Guid id)
    {
        if (id == Guid.Empty) throw new ArgumentException("Category identifier cannot be empty.", nameof(id));
    }
}
