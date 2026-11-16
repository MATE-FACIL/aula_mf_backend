using AulaVirtual.Modules.Courses.Application.Content;
using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Exams.Application.Practice;

namespace AulaVirtual.Modules.Attendance.Application;

// The teacher's side of the practice exercises (SCRUM-59 for teachers): a teacher edits the exercises of the lessons of a
// course ONLY if they are assigned to at least one group of that course. Any other course answers "not found", so
// a teacher cannot tell whether a course they do not teach exists. The editing itself is the team's use case.
public sealed class TeacherPracticeUseCases(
    ITeachingDirectory teaching,
    ICourseContentDirectory content,
    PracticeAdminUseCases practice)
{
    private const string NotAssigned = "No tienes asignado ese curso.";

    /// <summary>The published lessons of a course the teacher teaches; null otherwise. Courses DbContext: one query after the other (2 + 4).</summary>
    public async Task<CourseContentView?> GetLessonsAsync(Guid teacherUserId, Guid courseId, CancellationToken cancellationToken) =>
        await TeachesAsync(teacherUserId, courseId, cancellationToken)
            ? await content.GetPublishedContentAsync(courseId, cancellationToken)
            : null;

    public async Task<PracticeResult<IReadOnlyList<ExerciseAdminView>>> GetExercisesAsync(Guid teacherUserId, Guid courseId, Guid lessonId,
        CancellationToken cancellationToken) =>
        await TeachesAsync(teacherUserId, courseId, cancellationToken)
            ? await practice.GetAsync(courseId, lessonId, cancellationToken)
            : PracticeResult<IReadOnlyList<ExerciseAdminView>>.NotFound(NotAssigned);

    public async Task<PracticeResult<IReadOnlyList<ExerciseAdminView>>> ReplaceExercisesAsync(Guid teacherUserId, Guid courseId, Guid lessonId,
        IReadOnlyList<ExerciseInput>? inputs, CancellationToken cancellationToken) =>
        await TeachesAsync(teacherUserId, courseId, cancellationToken)
            ? await practice.ReplaceAsync(courseId, lessonId, inputs, cancellationToken)
            : PracticeResult<IReadOnlyList<ExerciseAdminView>>.NotFound(NotAssigned);

    private async Task<bool> TeachesAsync(Guid teacherUserId, Guid courseId, CancellationToken cancellationToken) =>
        (await teaching.GetGroupsAsync(teacherUserId, cancellationToken)).Any(group => group.CourseId == courseId);
}
