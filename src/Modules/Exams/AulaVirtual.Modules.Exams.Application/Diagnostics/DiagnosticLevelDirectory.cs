using AulaVirtual.Modules.Enrollments.Application;
using AulaVirtual.Modules.Exams.Domain.Diagnostics;

namespace AulaVirtual.Modules.Exams.Application.Diagnostics;

// Serves Enrollments (SCRUM-45): where a student's entry diagnostics stand, to place them in a group. One query.
public sealed class DiagnosticLevelDirectory(IDiagnosticRepository repository) : IDiagnosticLevelDirectory
{
    public async Task<IReadOnlyList<DiagnosticLevelInfo>> GetAsync(Guid studentUserId, CancellationToken cancellationToken) =>
        (await repository.GetAttemptsOfAsync(studentUserId, cancellationToken))
        .Select(attempt => new DiagnosticLevelInfo(attempt.Stage, DiagnosticLabels.Status(attempt.Status), attempt.SuggestedLevel,
            attempt.ConfirmedLevel, attempt.Id))
        .ToArray();
}
