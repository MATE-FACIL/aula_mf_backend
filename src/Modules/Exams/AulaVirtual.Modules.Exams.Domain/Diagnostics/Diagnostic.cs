using AulaVirtual.Modules.Exams.Domain.Practice;

namespace AulaVirtual.Modules.Exams.Domain.Diagnostics;

// Stage and level codes, the same the Mathematics levels use (Courses, SCRUM-39). Kept as codes here so this
// module does not depend on another module's domain.
public static class DiagnosticStages
{
    public const string Primary = "primary";
    public const string Secondary = "secondary";
    public static readonly IReadOnlyList<string> All = [Primary, Secondary];

    public static string? Parse(string? code) => All.FirstOrDefault(stage => stage == code?.Trim().ToLowerInvariant());
}

public static class DiagnosticLevels
{
    /// <summary>In path order: a student who masters a level is ready for the next one.</summary>
    public static readonly IReadOnlyList<string> All = ["prebasic", "basic", "intermediate", "advanced"];

    public static string? Parse(string? code) => All.FirstOrDefault(level => level == code?.Trim().ToLowerInvariant());
}

/// <summary>
/// A question of the entry diagnostic (SCRUM-47/48): a corrected question like a lesson exercise, plus the stage it
/// belongs to, the level it measures and its topic (the result is given per topic). Removed questions are archived,
/// not deleted: attempts already started keep them.
/// </summary>
public sealed class DiagnosticQuestion
{
    public const int MaxTopicLength = 80;

    private DiagnosticQuestion()
    {
    }

    private DiagnosticQuestion(Guid id, string stage, DateTimeOffset createdAt)
    {
        Id = id;
        Stage = stage;
        CreatedAt = createdAt;
    }

    public Guid Id { get; private set; }
    public string Stage { get; private set; } = string.Empty;
    public string Level { get; private set; } = string.Empty;
    public string Topic { get; private set; } = string.Empty;
    public int Position { get; private set; }
    public ExerciseKind Kind { get; private set; }
    public string Prompt { get; private set; } = string.Empty;
    public string[] Options { get; private set; } = [];
    public int? CorrectOption { get; private set; }
    public decimal? CorrectNumber { get; private set; }
    public decimal Tolerance { get; private set; }
    public string? Explanation { get; private set; }
    public bool IsArchived { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? UpdatedAt { get; private set; }

    public static DiagnosticQuestion Create(Guid id, string stage, string level, string topic, int position,
        ExerciseContent content, DateTimeOffset createdAt)
    {
        if (id == Guid.Empty) throw new ArgumentException("An identifier is required.", nameof(id));
        var question = new DiagnosticQuestion(id, DiagnosticStages.Parse(stage) ?? throw new ArgumentException("Etapa desconocida.", nameof(stage)),
            createdAt);
        question.Apply(level, topic, position, content);
        return question;
    }

    public void Update(string level, string topic, int position, ExerciseContent content, DateTimeOffset now)
    {
        Apply(level, topic, position, content);
        IsArchived = false;
        UpdatedAt = now;
    }

    public void Archive(DateTimeOffset now)
    {
        if (IsArchived) return;
        IsArchived = true;
        UpdatedAt = now;
    }

    public bool? Check(string? answer) => ExerciseRules.Check(Kind, Options, CorrectOption, CorrectNumber, Tolerance, answer);

    private void Apply(string level, string topic, int position, ExerciseContent content)
    {
        var parsedLevel = DiagnosticLevels.Parse(level)
                          ?? throw new ArgumentException("El nivel es «prebasic», «basic», «intermediate» o «advanced».", nameof(level));
        var trimmedTopic = topic?.Trim();
        if (string.IsNullOrEmpty(trimmedTopic)) throw new ArgumentException("El tema es obligatorio.", nameof(topic));
        if (trimmedTopic.Length > MaxTopicLength)
            throw new ArgumentException($"El tema admite como máximo {MaxTopicLength} caracteres.", nameof(topic));
        if (position < 1) throw new ArgumentOutOfRangeException(nameof(position), "Positions start at 1.");
        var rules = ExerciseRules.Normalize(content);

        Level = parsedLevel;
        Topic = trimmedTopic;
        Position = position;
        Kind = rules.Kind;
        Prompt = rules.Prompt;
        Explanation = rules.Explanation;
        Options = rules.Options;
        CorrectOption = rules.CorrectOption;
        CorrectNumber = rules.CorrectNumber;
        Tolerance = rules.Tolerance;
    }
}

public enum DiagnosticStatus
{
    InProgress,
    Completed
}

/// <summary>
/// A student's entry diagnostic for one stage (SCRUM-48). The questions are copied when it starts, so editing the
/// bank later does not change a test already under way. One per student and stage. When the last question is
/// answered it is scored and a level is suggested; a teacher then confirms that level or picks another one.
/// </summary>
public sealed class DiagnosticAttempt
{
    private DiagnosticAttempt()
    {
    }

    private DiagnosticAttempt(Guid id, Guid studentUserId, string stage, Guid[] questionIds, DateTimeOffset startedAt)
    {
        Id = id;
        StudentUserId = studentUserId;
        Stage = stage;
        QuestionIds = questionIds;
        Status = DiagnosticStatus.InProgress;
        StartedAt = startedAt;
    }

    public Guid Id { get; private set; }
    public Guid StudentUserId { get; private set; }
    public string Stage { get; private set; } = string.Empty;
    /// <summary>The questions of this attempt, in the order they are asked.</summary>
    public Guid[] QuestionIds { get; private set; } = [];
    public DiagnosticStatus Status { get; private set; }
    public DateTimeOffset StartedAt { get; private set; }
    public DateTimeOffset? CompletedAt { get; private set; }
    public string? SuggestedLevel { get; private set; }
    public string? ConfirmedLevel { get; private set; }
    public Guid? ConfirmedByUserId { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }

    public static DiagnosticAttempt Start(Guid id, Guid studentUserId, string stage, IReadOnlyList<Guid> questionIds,
        DateTimeOffset startedAt)
    {
        if (id == Guid.Empty || studentUserId == Guid.Empty) throw new ArgumentException("Identifiers are required.");
        var parsed = DiagnosticStages.Parse(stage) ?? throw new ArgumentException("Etapa desconocida.", nameof(stage));
        if (questionIds.Count == 0) throw new ArgumentException("A diagnostic needs questions.", nameof(questionIds));
        if (questionIds.Distinct().Count() != questionIds.Count) throw new ArgumentException("Questions cannot repeat.", nameof(questionIds));
        return new DiagnosticAttempt(id, studentUserId, parsed, [.. questionIds], startedAt);
    }

    public void Complete(string suggestedLevel, DateTimeOffset now)
    {
        if (Status == DiagnosticStatus.Completed) throw new InvalidOperationException("The diagnostic is already completed.");
        SuggestedLevel = DiagnosticLevels.Parse(suggestedLevel) ?? throw new ArgumentException("Nivel desconocido.", nameof(suggestedLevel));
        Status = DiagnosticStatus.Completed;
        CompletedAt = now;
    }

    /// <summary>The teacher's decision: the suggested level or another one. Can be changed later.</summary>
    public void Confirm(string level, Guid reviewerUserId, DateTimeOffset now)
    {
        if (Status != DiagnosticStatus.Completed)
            throw new InvalidOperationException("El alumno todavía no termina el diagnóstico.");
        if (reviewerUserId == Guid.Empty) throw new ArgumentException("A reviewer is required.", nameof(reviewerUserId));
        ConfirmedLevel = DiagnosticLevels.Parse(level) ?? throw new ArgumentException("Nivel desconocido.", nameof(level));
        ConfirmedByUserId = reviewerUserId;
        ConfirmedAt = now;
    }
}

/// <summary>
/// One answer of a diagnostic. The topic and level are copied from the question, so the result stays the same even
/// if the bank is edited later. <see cref="Answer"/> is null when the student chose «No lo sé» (counts as wrong).
/// </summary>
public sealed class DiagnosticAnswer
{
    private DiagnosticAnswer()
    {
    }

    private DiagnosticAnswer(Guid id, Guid attemptId, DiagnosticQuestion question, string? answer, bool isCorrect,
        DateTimeOffset answeredAt)
    {
        Id = id;
        AttemptId = attemptId;
        QuestionId = question.Id;
        Topic = question.Topic;
        Level = question.Level;
        Answer = answer;
        IsCorrect = isCorrect;
        AnsweredAt = answeredAt;
    }

    public Guid Id { get; private set; }
    public Guid AttemptId { get; private set; }
    public Guid QuestionId { get; private set; }
    public string Topic { get; private set; } = string.Empty;
    public string Level { get; private set; } = string.Empty;
    public string? Answer { get; private set; }
    public bool IsCorrect { get; private set; }
    public DateTimeOffset AnsweredAt { get; private set; }

    public static DiagnosticAnswer Record(Guid id, DiagnosticAttempt attempt, DiagnosticQuestion question, string? answer,
        bool isCorrect, DateTimeOffset answeredAt)
    {
        ArgumentNullException.ThrowIfNull(attempt);
        ArgumentNullException.ThrowIfNull(question);
        if (id == Guid.Empty) throw new ArgumentException("An identifier is required.", nameof(id));
        if (!attempt.QuestionIds.Contains(question.Id)) throw new ArgumentException("The question is not part of this diagnostic.");
        if (answer is null && isCorrect) throw new ArgumentException("A skipped question cannot be correct.");
        return new DiagnosticAnswer(id, attempt.Id, question, answer?.Trim(), isCorrect, answeredAt);
    }
}

public sealed record ScoreLine(string Key, int Correct, int Total)
{
    public int Percent => Total == 0 ? 0 : (int)Math.Round(Correct * 100.0 / Total, MidpointRounding.AwayFromZero);
}

public sealed record DiagnosticScore(IReadOnlyList<ScoreLine> Topics, IReadOnlyList<ScoreLine> Levels, string SuggestedLevel);

// How a finished diagnostic is read (SCRUM-48). Per topic, in the order the topics first appear; per level, in path
// order. The suggested level is the first level the student does not master yet (below the pass mark); a student
// who masters every measured level is suggested the highest one. The teacher always has the last word.
public static class DiagnosticScoring
{
    /// <summary>Percentage of a level's questions needed to master it.</summary>
    public const int PassPercent = 60;

    /// <param name="answers">The attempt's answers, in the order of its questions.</param>
    public static DiagnosticScore Score(IReadOnlyList<DiagnosticAnswer> answers)
    {
        if (answers.Count == 0) throw new ArgumentException("There is nothing to score.", nameof(answers));
        var topics = answers.GroupBy(answer => answer.Topic, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new ScoreLine(group.First().Topic, group.Count(answer => answer.IsCorrect), group.Count()))
            .ToArray();
        var levels = DiagnosticLevels.All
            .Select(level => answers.Where(answer => answer.Level == level).ToArray())
            .Where(group => group.Length > 0)
            .Select(group => new ScoreLine(group[0].Level, group.Count(answer => answer.IsCorrect), group.Length))
            .ToArray();
        var suggested = levels.FirstOrDefault(line => line.Percent < PassPercent) ?? levels[^1];
        return new DiagnosticScore(topics, levels, suggested.Key);
    }
}
