using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Courses.Application.Levels;
using AulaVirtual.Modules.Courses.Application.Offerings;
using AulaVirtual.Modules.Courses.Domain.Levels;
using AulaVirtual.Modules.Enrollments.Domain;
using AulaVirtual.Modules.Users.Application.Abstractions;

namespace AulaVirtual.Modules.Enrollments.Application;

/// <summary>A student's entry diagnostic of one stage, as Enrollments needs it (level codes: «prebasic»…«advanced»).</summary>
public sealed record DiagnosticLevelInfo(string Stage, string Status, string? SuggestedLevel, string? ConfirmedLevel, Guid AttemptId);

// Cross-module read contract (served by Exams): where the student's diagnostics stand. One query.
public interface IDiagnosticLevelDirectory
{
    Task<IReadOnlyList<DiagnosticLevelInfo>> GetAsync(Guid studentUserId, CancellationToken cancellationToken);
}

public sealed record PlacementLevel(string Code, string Label);

/// <param name="Status"><c>in_progress</c> or <c>completed</c>.</param>
/// <param name="AttemptId">To open the diagnostic and confirm its level.</param>
public sealed record PlacementDiagnostic(Guid AttemptId, string Stage, string StageLabel, string Status, PlacementLevel? Suggested, PlacementLevel? Confirmed);

public sealed record PlacementEnrollment(Guid EnrollmentId, Guid OfferingId, string CourseTitle, string OfferingName, string Status);

/// <param name="Recommended">The group is of the level the teacher confirmed in the diagnostic of its stage.</param>
/// <param name="AlreadyEnrolled">The student already holds a seat (pending or active) in this group.</param>
public sealed record PlacementGroup(
    Guid OfferingId,
    Guid CourseId,
    string CourseTitle,
    string OfferingName,
    string StageLabel,
    string LevelLabel,
    int MaximumCapacity,
    int TakenSeats,
    int FreeSeats,
    bool Recommended,
    bool AlreadyEnrolled);

public sealed record Placement(
    Guid StudentUserId,
    string StudentName,
    IReadOnlyList<PlacementDiagnostic> Diagnostics,
    IReadOnlyList<PlacementEnrollment> Enrollments,
    IReadOnlyList<PlacementGroup> Groups);

// Where to place a student (SCRUM-45): the published Mathematics groups with their free seats, those of the level the
// teacher confirmed in the diagnostic first. The administrator decides: enrolling or moving still goes through the
// usual operations, which validate the capacity again. A diagnostic the teacher has not confirmed yet recommends
// nothing (its suggested level is shown, not used). Seven queries whatever the number of groups.
public sealed class PlacementUseCases(
    IEnrollmentRepository enrollments,
    IDiagnosticLevelDirectory diagnostics,
    IOfferingDirectory offerings,
    IMathLevelDirectory mathLevels,
    IUserDirectory users,
    IRoleDirectory roles)
{
    public async Task<EnrollmentResult<Placement>> GetAsync(Guid studentUserId, CancellationToken cancellationToken)
    {
        // Four different modules (Users, Authorization, Exams and Courses): the four queries run at the same time.
        var personTask = users.GetAsync(studentUserId, cancellationToken);
        var rolesTask = roles.GetRoleCodesByUserIdsAsync([studentUserId], cancellationToken);
        var diagnosticsTask = diagnostics.GetAsync(studentUserId, cancellationToken);
        var offeringsTask = offerings.ListAsync(cancellationToken);
        await Task.WhenAll(personTask, rolesTask, diagnosticsTask, offeringsTask);

        if (personTask.Result is not { } person)
        {
            return EnrollmentResult<Placement>.NotFound("No encontramos a esa persona.");
        }

        if (!(rolesTask.Result.GetValueOrDefault(studentUserId)?.Contains(Roles.Student) ?? false))
        {
            return EnrollmentResult<Placement>.Invalid("Solo se puede ubicar a personas con rol de alumno.");
        }

        var published = offeringsTask.Result.Where(offering => offering.IsPublished).ToArray();
        var levelsTask = mathLevels.GetLevelsAsync(published.Select(offering => offering.CourseId).Distinct().ToArray(),
            cancellationToken);
        // Same DbContext (Enrollments): one after the other, while Courses answers in parallel.
        var live = await enrollments.GetLiveByStudentsAsync([studentUserId], cancellationToken);
        var levelOfCourse = (await levelsTask).ToDictionary(level => level.CourseId);
        var groupsOffered = published.Where(offering => levelOfCourse.ContainsKey(offering.CourseId)).ToArray();
        var seats = await enrollments.CountSeatsAsync(groupsOffered.Select(offering => offering.Id).ToArray(), cancellationToken);

        var confirmed = diagnosticsTask.Result
            .Where(info => info.ConfirmedLevel is not null)
            .ToDictionary(info => info.Stage, info => info.ConfirmedLevel!);
        var heldGroups = live.Select(enrollment => enrollment.CourseOfferingId).ToHashSet();
        var order = MathLevels.All.Select((definition, index) => (definition.StageCode, definition.LevelCode, index))
            .ToDictionary(item => (item.StageCode, item.LevelCode), item => item.index);

        var groups = groupsOffered
            .Select(offering =>
            {
                var level = levelOfCourse[offering.CourseId];
                var taken = seats.GetValueOrDefault(offering.Id);
                var recommended = confirmed.TryGetValue(level.Stage, out var code) && code == level.Level;
                return (Position: order.GetValueOrDefault((level.Stage, level.Level), int.MaxValue), Group: new PlacementGroup(
                    offering.Id, offering.CourseId, offering.CourseTitle, offering.Name, level.StageLabel, level.LevelLabel,
                    offering.MaximumCapacity, taken, Math.Max(0, offering.MaximumCapacity - taken), recommended,
                    heldGroups.Contains(offering.Id)));
            })
            .OrderByDescending(item => item.Group.Recommended)
            .ThenBy(item => item.Position)
            .ThenBy(item => item.Group.OfferingName, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => item.Group)
            .ToArray();

        var editions = offeringsTask.Result.ToDictionary(offering => offering.Id);
        var held = live.Select(enrollment =>
        {
            var edition = editions.GetValueOrDefault(enrollment.CourseOfferingId);
            return new PlacementEnrollment(enrollment.Id, enrollment.CourseOfferingId, edition?.CourseTitle ?? "(curso eliminado)",
                edition?.Name ?? "(edición eliminada)", EnrollmentStatusCodes.From(enrollment.Status));
        }).ToArray();

        var summaries = DiagnosticStageOrder(diagnosticsTask.Result).Select(info => new PlacementDiagnostic(info.AttemptId, info.Stage,
            StageLabel(info.Stage), info.Status, LevelOf(info.SuggestedLevel), LevelOf(info.ConfirmedLevel))).ToArray();

        return EnrollmentResult<Placement>.Ok(new Placement(studentUserId, person.FullName, summaries, held, groups));
    }

    private static IEnumerable<DiagnosticLevelInfo> DiagnosticStageOrder(IReadOnlyList<DiagnosticLevelInfo> items) =>
        items.OrderBy(info => info.Stage == "primary" ? 0 : 1);

    private static string StageLabel(string stage) =>
        MathLevels.All.FirstOrDefault(definition => definition.StageCode == stage)?.StageLabel ?? stage;

    private static PlacementLevel? LevelOf(string? code) => code is null
        ? null
        : new PlacementLevel(code, MathLevels.All.FirstOrDefault(definition => definition.LevelCode == code)?.LevelLabel ?? code);
}
