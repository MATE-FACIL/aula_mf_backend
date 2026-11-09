namespace AulaVirtual.Modules.Courses.Domain.Levels;

public enum EducationStage
{
    Primary,
    Secondary
}

public enum MathLevel
{
    PreBasic,
    Basic,
    Intermediate,
    Advanced
}

/// <summary>One of the eight Mathematics levels: a stage, a level, its label and its class length.</summary>
public sealed record MathLevelDefinition(EducationStage Stage, MathLevel Level)
{
    public string StageCode => MathLevels.StageCode(Stage);
    public string LevelCode => MathLevels.LevelCode(Level);
    public string StageLabel => Stage == EducationStage.Primary ? "Primaria" : "Secundaria";
    public string LevelLabel => Level switch
    {
        MathLevel.PreBasic => "Prebásico",
        MathLevel.Basic => "Básico",
        MathLevel.Intermediate => "Intermedio",
        _ => "Avanzado"
    };
    public string CourseTitle => $"Matemáticas · {StageLabel} {LevelLabel}";
    // SCRUM-42: primary classes last 60 minutes, secondary 90.
    public int SessionMinutes => Stage == EducationStage.Primary ? 60 : 90;
}

// The Mathematics path (SCRUM-39): four levels per stage (Prebásico, Básico, Intermedio, Avanzado) for primary and
// secondary, each with its 14 sessions. Each level is an ordinary course, so groups, lessons, enrollments and
// attendance work for it unchanged; this catalog only says which course is which level.
public static class MathLevels
{
    public const int SessionsPerLevel = 14;

    public static readonly IReadOnlyList<MathLevelDefinition> All =
        [.. from stage in Enum.GetValues<EducationStage>()
            from level in Enum.GetValues<MathLevel>()
            select new MathLevelDefinition(stage, level)];

    public static string StageCode(EducationStage stage) => stage == EducationStage.Primary ? "primary" : "secondary";

    public static string LevelCode(MathLevel level) => level switch
    {
        MathLevel.PreBasic => "prebasic",
        MathLevel.Basic => "basic",
        MathLevel.Intermediate => "intermediate",
        _ => "advanced"
    };
}

/// <summary>Which course is a given level. One course per level, and a course is at most one level.</summary>
public sealed class MathLevelCourse
{
    private MathLevelCourse()
    {
    }

    private MathLevelCourse(EducationStage stage, MathLevel level, Guid courseId, DateTimeOffset createdAt)
    {
        Stage = stage;
        Level = level;
        CourseId = courseId;
        CreatedAt = createdAt;
    }

    public EducationStage Stage { get; private set; }
    public MathLevel Level { get; private set; }
    public Guid CourseId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    public static MathLevelCourse Create(EducationStage stage, MathLevel level, Guid courseId, DateTimeOffset createdAt)
    {
        if (courseId == Guid.Empty) throw new ArgumentException("A course identifier is required.", nameof(courseId));
        if (!Enum.IsDefined(stage) || !Enum.IsDefined(level)) throw new ArgumentOutOfRangeException(nameof(level));
        return new MathLevelCourse(stage, level, courseId, createdAt);
    }
}
