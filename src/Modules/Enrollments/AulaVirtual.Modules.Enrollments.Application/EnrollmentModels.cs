using AulaVirtual.Modules.Enrollments.Domain;

namespace AulaVirtual.Modules.Enrollments.Application;

public static class EnrollmentStatusCodes
{
    public const string Pending = "pending";
    public const string Active = "active";
    public const string Withdrawn = "withdrawn";

    public static string From(EnrollmentStatus status) => status switch
    {
        EnrollmentStatus.Pending => Pending,
        EnrollmentStatus.Active => Active,
        EnrollmentStatus.Withdrawn => Withdrawn,
        _ => throw new ArgumentOutOfRangeException(nameof(status))
    };

    public static bool TryParse(string? value, out EnrollmentStatus? status)
    {
        status = value switch
        {
            Pending => EnrollmentStatus.Pending,
            Active => EnrollmentStatus.Active,
            Withdrawn => EnrollmentStatus.Withdrawn,
            _ => null
        };
        return value is null || status is not null;
    }
}

public sealed record EnrollmentListQuery(
    Guid? CourseOfferingId,
    Guid? StudentUserId,
    string? Status,
    int Page = 1,
    int PageSize = EnrollmentListQuery.DefaultPageSize)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;
}

public sealed record EnrollmentPage(IReadOnlyList<EnrollmentView> Items, int Page, int PageSize, int Total);

public sealed record EnrollmentView(
    Guid Id,
    Guid StudentUserId,
    string StudentName,
    string? StudentDocument,
    Guid CourseOfferingId,
    string CourseTitle,
    string OfferingName,
    string Status,
    string? PaymentNote,
    DateTimeOffset CreatedAt,
    string? CreatedByName,
    DateTimeOffset? ActivatedAt,
    string? ActivatedByName,
    DateTimeOffset? WithdrawnAt,
    string? WithdrawnByName);

/// <summary>An edition with its seats: the base for «group full» warnings.</summary>
public sealed record OfferingSeats(
    Guid Id,
    Guid CourseId,
    string CourseTitle,
    string Name,
    bool IsPublished,
    int MaximumCapacity,
    int TakenSeats)
{
    public int FreeSeats => Math.Max(0, MaximumCapacity - TakenSeats);
}

public enum EnrollmentResultStatus
{
    Ok,
    NotFound,
    Invalid,
    Conflict
}

public sealed record EnrollmentResult<T>(EnrollmentResultStatus Status, T? Value = default, string? Detail = null)
{
    public static EnrollmentResult<T> Ok(T value) => new(EnrollmentResultStatus.Ok, value);

    public static EnrollmentResult<T> NotFound(string detail) => new(EnrollmentResultStatus.NotFound, Detail: detail);

    public static EnrollmentResult<T> Invalid(string detail) => new(EnrollmentResultStatus.Invalid, Detail: detail);

    public static EnrollmentResult<T> Conflict(string detail) => new(EnrollmentResultStatus.Conflict, Detail: detail);
}
