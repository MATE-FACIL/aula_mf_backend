using AulaVirtual.Modules.Courses.Application.Content;
using AulaVirtual.Modules.Enrollments.Application;
using AulaVirtual.Modules.Exams.Domain.Practice;

namespace AulaVirtual.Modules.Exams.Application.Practice;

// The team's side of practice (SCRUM-59): the exercises of a lesson are edited as one ordered list. Exercises
// left out are archived, not deleted, so students keep their history and their progress stays honest.
public sealed class PracticeAdminUseCases(IPracticeRepository repository, ILessonDirectory lessons, TimeProvider timeProvider)
{
    public const int MaxExercisesPerLesson = 30;
    private const string LessonNotFound = "No encontramos esa lección en el curso.";

    public async Task<PracticeResult<IReadOnlyList<ExerciseAdminView>>> GetAsync(Guid courseId, Guid lessonId,
        CancellationToken cancellationToken)
    {
        if (!await BelongsAsync(courseId, lessonId, cancellationToken))
            return PracticeResult<IReadOnlyList<ExerciseAdminView>>.NotFound(LessonNotFound);
        return PracticeResult<IReadOnlyList<ExerciseAdminView>>.Ok(ToAdminViews(await repository.GetActiveAsync(lessonId, cancellationToken)));
    }

    public async Task<PracticeResult<IReadOnlyList<ExerciseAdminView>>> ReplaceAsync(Guid courseId, Guid lessonId,
        IReadOnlyList<ExerciseInput>? inputs, CancellationToken cancellationToken)
    {
        inputs ??= [];
        if (inputs.Count > MaxExercisesPerLesson)
            return PracticeResult<IReadOnlyList<ExerciseAdminView>>.Invalid($"Una lección admite como máximo {MaxExercisesPerLesson} ejercicios.");
        var ids = inputs.Where(input => input.Id is not null).Select(input => input.Id!.Value).ToArray();
        if (ids.Distinct().Count() != ids.Length)
            return PracticeResult<IReadOnlyList<ExerciseAdminView>>.Invalid("Cada ejercicio puede aparecer una sola vez.");
        if (!await BelongsAsync(courseId, lessonId, cancellationToken))
            return PracticeResult<IReadOnlyList<ExerciseAdminView>>.NotFound(LessonNotFound);

        var existing = (await repository.GetAllAsync(lessonId, cancellationToken)).ToDictionary(exercise => exercise.Id);
        if (ids.FirstOrDefault(id => !existing.ContainsKey(id)) is var unknown && unknown != Guid.Empty)
            return PracticeResult<IReadOnlyList<ExerciseAdminView>>.Invalid("Uno de los ejercicios no pertenece a esta lección.");

        var now = timeProvider.GetUtcNow();
        try
        {
            for (var index = 0; index < inputs.Count; index++)
            {
                var input = inputs[index];
                if (!ExerciseKinds.TryParse(input.Kind, out var kind))
                    return PracticeResult<IReadOnlyList<ExerciseAdminView>>.Invalid($"Ejercicio {index + 1}: el tipo es «multiple_choice» o «numeric».");
                var content = new ExerciseContent(kind, input.Prompt, input.Options, input.CorrectOption, input.CorrectNumber,
                    input.Tolerance, input.Explanation);
                if (input.Id is { } id) existing[id].Update(content, index + 1, now);
                else repository.Add(Exercise.Create(Guid.NewGuid(), lessonId, courseId, index + 1, content, now));
            }
        }
        catch (ArgumentException exception)
        {
            return PracticeResult<IReadOnlyList<ExerciseAdminView>>.Invalid(Clean(exception.Message));
        }

        foreach (var exercise in existing.Values.Where(exercise => !ids.Contains(exercise.Id))) exercise.Archive(now);
        await repository.SaveChangesAsync(cancellationToken);
        return PracticeResult<IReadOnlyList<ExerciseAdminView>>.Ok(ToAdminViews(await repository.GetActiveAsync(lessonId, cancellationToken)));
    }

    private async Task<bool> BelongsAsync(Guid courseId, Guid lessonId, CancellationToken cancellationToken) =>
        await lessons.GetLessonAsync(lessonId, cancellationToken) is { } lesson && lesson.CourseId == courseId;

    private static IReadOnlyList<ExerciseAdminView> ToAdminViews(IReadOnlyList<Exercise> exercises) =>
        exercises.Select((exercise, index) => new ExerciseAdminView(exercise.Id, index + 1, ExerciseKinds.ToCode(exercise.Kind),
            exercise.Prompt, exercise.Options, exercise.CorrectOption, exercise.CorrectNumber, exercise.Tolerance,
            exercise.Explanation)).ToArray();

    internal static string Clean(string message)
    {
        var cut = message.IndexOf(" (Parameter", StringComparison.Ordinal);
        return cut > 0 ? message[..cut] : message;
    }
}

// The student's side (SCRUM-59/60): practice of a published lesson of a course where the student has an ACTIVE
// enrollment. Answers are corrected here and every attempt is kept. Fixed cost per request whatever the number
// of exercises or lessons.
public sealed class StudentPracticeUseCases(
    IPracticeRepository repository,
    ILessonDirectory lessons,
    IStudentCourseAccess access,
    TimeProvider timeProvider) : IPracticeProgressDirectory
{
    // Enough to practise freely, low enough that nobody fills the history by brute force.
    public const int MaxAttemptsPerExercise = 50;
    public const int MaxPageSize = 100;
    private const string NotAvailable = "Esta práctica no está disponible para ti.";

    /// <summary>Lesson (1) + access (2) + exercises (1) + the student's status (1).</summary>
    public async Task<PracticeResult<LessonPractice>> GetLessonAsync(Guid studentUserId, Guid lessonId,
        CancellationToken cancellationToken)
    {
        var lesson = await lessons.GetLessonAsync(lessonId, cancellationToken);
        if (lesson is not { IsPublished: true } || !await access.CanStudyAsync(studentUserId, lesson.CourseId, cancellationToken))
            return PracticeResult<LessonPractice>.NotFound(NotAvailable);

        var exercises = await repository.GetActiveAsync(lessonId, cancellationToken);
        var status = exercises.Count == 0
            ? new Dictionary<Guid, ExerciseStatus>()
            : (await repository.GetStatusAsync(studentUserId, exercises.Select(exercise => exercise.Id).ToArray(), cancellationToken))
                .ToDictionary(item => item.ExerciseId);

        var views = exercises.Select((exercise, index) =>
        {
            var own = status.GetValueOrDefault(exercise.Id);
            return new ExerciseStudentView(exercise.Id, index + 1, ExerciseKinds.ToCode(exercise.Kind), exercise.Prompt,
                exercise.Options, own?.Attempts ?? 0, own?.Solved ?? false);
        }).ToArray();
        return PracticeResult<LessonPractice>.Ok(new LessonPractice(lesson.LessonId, lesson.Title,
            Progress.Of(views.Count(view => view.Solved), views.Length), views));
    }

    public async Task<PracticeResult<AnswerResult>> AnswerAsync(Guid studentUserId, Guid exerciseId, string? answer,
        CancellationToken cancellationToken)
    {
        var exercise = await repository.GetAsync(exerciseId, cancellationToken);
        if (exercise is null || exercise.IsArchived) return PracticeResult<AnswerResult>.NotFound(NotAvailable);
        var lesson = await lessons.GetLessonAsync(exercise.LessonId, cancellationToken);
        if (lesson is not { IsPublished: true } || !await access.CanStudyAsync(studentUserId, exercise.CourseId, cancellationToken))
            return PracticeResult<AnswerResult>.NotFound(NotAvailable);

        var isCorrect = exercise.Check(answer);
        if (isCorrect is null)
            return PracticeResult<AnswerResult>.Invalid(exercise.Kind == ExerciseKind.MultipleChoice
                ? "Elige una de las opciones."
                : "Escribe un número, por ejemplo 3,5.");
        var previous = await repository.CountAttemptsAsync(studentUserId, exerciseId, cancellationToken);
        if (previous >= MaxAttemptsPerExercise)
            return PracticeResult<AnswerResult>.Conflict("Ya respondiste este ejercicio muchas veces. Pide ayuda a tu profesor.");

        repository.Add(ExerciseAttempt.Record(Guid.NewGuid(), exercise, studentUserId, answer!, isCorrect.Value, timeProvider.GetUtcNow()));
        await repository.SaveChangesAsync(cancellationToken);

        var exercises = await repository.GetActiveAsync(exercise.LessonId, cancellationToken);
        var status = await repository.GetStatusAsync(studentUserId, exercises.Select(item => item.Id).ToArray(), cancellationToken);
        return PracticeResult<AnswerResult>.Ok(new AnswerResult(isCorrect.Value, exercise.Explanation,
            Progress.Of(status.Count(item => item.Solved), exercises.Count), previous + 1));
    }

    /// <summary>Progress in a course: per published lesson and overall. Access (2) + lessons (1) + counts (2).</summary>
    public async Task<PracticeResult<CourseProgress>> GetCourseProgressAsync(Guid studentUserId, Guid courseId,
        CancellationToken cancellationToken)
    {
        if (!await access.CanStudyAsync(studentUserId, courseId, cancellationToken))
            return PracticeResult<CourseProgress>.NotFound(NotAvailable);

        var published = await lessons.GetPublishedLessonsAsync(courseId, cancellationToken);
        var (totals, solved) = await CountAsync([studentUserId], published, cancellationToken);
        var perLesson = published.Select(lesson => new LessonProgress(lesson.LessonId, lesson.Title,
            Progress.Of(solved.GetValueOrDefault((studentUserId, lesson.LessonId)), totals.GetValueOrDefault(lesson.LessonId)))).ToArray();
        return PracticeResult<CourseProgress>.Ok(new CourseProgress(courseId,
            Progress.Of(perLesson.Sum(item => item.Progress.Solved), perLesson.Sum(item => item.Progress.Total)), perLesson));
    }

    public async Task<AttemptPage> GetAttemptsAsync(Guid studentUserId, Guid? lessonId, int page, int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);
        var (items, total) = await repository.GetAttemptsAsync(studentUserId, lessonId, page, pageSize, cancellationToken);
        return new AttemptPage(items, page, pageSize, total);
    }

    /// <summary>For the teacher panel: lessons (1) + counts (2), whatever the number of students.</summary>
    public async Task<IReadOnlyDictionary<Guid, Progress>> GetCourseProgressAsync(IReadOnlyCollection<Guid> studentUserIds,
        Guid courseId, CancellationToken cancellationToken)
    {
        if (studentUserIds.Count == 0) return new Dictionary<Guid, Progress>();
        var published = await lessons.GetPublishedLessonsAsync(courseId, cancellationToken);
        var (totals, solved) = await CountAsync(studentUserIds, published, cancellationToken);
        var total = published.Sum(lesson => totals.GetValueOrDefault(lesson.LessonId));
        return studentUserIds.ToDictionary(student => student, student =>
            Progress.Of(published.Sum(lesson => solved.GetValueOrDefault((student, lesson.LessonId))), total));
    }

    private async Task<(IReadOnlyDictionary<Guid, int> Totals, IReadOnlyDictionary<(Guid, Guid), int> Solved)> CountAsync(
        IReadOnlyCollection<Guid> studentUserIds, IReadOnlyList<LessonRef> published, CancellationToken cancellationToken)
    {
        if (published.Count == 0) return (new Dictionary<Guid, int>(), new Dictionary<(Guid, Guid), int>());
        var lessonIds = published.Select(lesson => lesson.LessonId).ToArray();
        // Same DbContext (Exams): one after the other.
        var totals = await repository.CountActiveAsync(lessonIds, cancellationToken);
        var solved = await repository.CountSolvedAsync(studentUserIds, lessonIds, cancellationToken);
        return (totals, solved.ToDictionary(pair => (pair.Key.StudentUserId, pair.Key.LessonId), pair => pair.Value));
    }
}
