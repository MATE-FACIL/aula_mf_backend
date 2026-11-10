using AulaVirtual.Modules.Exams.Domain.Diagnostics;
using AulaVirtual.Modules.Exams.Domain.Practice;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations.Internal;

namespace AulaVirtual.Modules.Exams.Infrastructure.Persistence;

public sealed class ExamsDbContext(DbContextOptions<ExamsDbContext> options) : DbContext(options)
{
    public DbSet<Exercise> Exercises => Set<Exercise>();
    public DbSet<ExerciseAttempt> Attempts => Set<ExerciseAttempt>();
    public DbSet<DiagnosticQuestion> DiagnosticQuestions => Set<DiagnosticQuestion>();
    public DbSet<DiagnosticAttempt> DiagnosticAttempts => Set<DiagnosticAttempt>();
    public DbSet<DiagnosticAnswer> DiagnosticAnswers => Set<DiagnosticAnswer>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(ExamsDbOptions.Schema);

        var exercise = modelBuilder.Entity<Exercise>();
        exercise.ToTable("exercises", ExamsDbOptions.Schema, table =>
        {
            table.HasCheckConstraint("ck_exercises_kind", "kind IN ('multiple_choice', 'numeric')");
            table.HasCheckConstraint("ck_exercises_position", "position >= 1");
            table.HasCheckConstraint("ck_exercises_answer",
                "(kind = 'multiple_choice' AND correct_option IS NOT NULL) OR (kind = 'numeric' AND correct_number IS NOT NULL)");
        });
        exercise.HasKey(item => item.Id).HasName("pk_exercises");
        exercise.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        exercise.Property(item => item.LessonId).HasColumnName("lesson_id").IsRequired();
        exercise.Property(item => item.CourseId).HasColumnName("course_id").IsRequired();
        exercise.Property(item => item.Position).HasColumnName("position").IsRequired();
        exercise.Property(item => item.Kind).HasColumnName("kind").HasMaxLength(20)
            .HasConversion(value => ExerciseKinds.ToCode(value),
                value => value == ExerciseKinds.MultipleChoice ? ExerciseKind.MultipleChoice : ExerciseKind.Numeric)
            .IsRequired();
        exercise.Property(item => item.Prompt).HasColumnName("prompt").HasMaxLength(Exercise.MaxPromptLength).IsRequired();
        exercise.Property(item => item.Options).HasColumnName("options").HasColumnType("text[]").IsRequired();
        exercise.Property(item => item.CorrectOption).HasColumnName("correct_option");
        exercise.Property(item => item.CorrectNumber).HasColumnName("correct_number").HasPrecision(18, 6);
        exercise.Property(item => item.Tolerance).HasColumnName("tolerance").HasPrecision(18, 6).IsRequired();
        exercise.Property(item => item.Explanation).HasColumnName("explanation").HasMaxLength(Exercise.MaxExplanationLength);
        exercise.Property(item => item.IsArchived).HasColumnName("is_archived").IsRequired();
        exercise.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        exercise.Property(item => item.UpdatedAt).HasColumnName("updated_at");
        // A lesson's exercises in order, and the per-lesson counts of active ones.
        exercise.HasIndex(item => new { item.LessonId, item.IsArchived, item.Position }).HasDatabaseName("ix_exercises_lesson_active_position");

        var attempt = modelBuilder.Entity<ExerciseAttempt>();
        attempt.ToTable("exercise_attempts", ExamsDbOptions.Schema);
        attempt.HasKey(item => item.Id).HasName("pk_exercise_attempts");
        attempt.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        attempt.Property(item => item.ExerciseId).HasColumnName("exercise_id").IsRequired();
        attempt.Property(item => item.LessonId).HasColumnName("lesson_id").IsRequired();
        attempt.Property(item => item.CourseId).HasColumnName("course_id").IsRequired();
        attempt.Property(item => item.StudentUserId).HasColumnName("student_user_id").IsRequired();
        attempt.Property(item => item.Answer).HasColumnName("answer").HasMaxLength(Exercise.MaxAnswerLength).IsRequired();
        attempt.Property(item => item.IsCorrect).HasColumnName("is_correct").IsRequired();
        attempt.Property(item => item.AnsweredAt).HasColumnName("answered_at").IsRequired();
        attempt.HasOne<Exercise>().WithMany().HasForeignKey(item => item.ExerciseId)
            .HasConstraintName("fk_exercise_attempts_exercises_exercise_id").OnDelete(DeleteBehavior.Restrict);
        // A student's status per exercise and the attempt limit.
        attempt.HasIndex(item => new { item.StudentUserId, item.ExerciseId }).HasDatabaseName("ix_exercise_attempts_student_exercise");
        // Solved per (student, lesson): progress.
        attempt.HasIndex(item => new { item.StudentUserId, item.LessonId, item.IsCorrect }).HasDatabaseName("ix_exercise_attempts_student_lesson");
        // History, newest first.
        attempt.HasIndex(item => new { item.StudentUserId, item.AnsweredAt })
            .HasDatabaseName("ix_exercise_attempts_student_answered_at").IsDescending(false, true);
        attempt.HasIndex(item => item.ExerciseId).HasDatabaseName("ix_exercise_attempts_exercise");

        ConfigureDiagnostics(modelBuilder);
    }

    // Entry diagnostic (SCRUM-47/48).
    private static void ConfigureDiagnostics(ModelBuilder modelBuilder)
    {
        var question = modelBuilder.Entity<DiagnosticQuestion>();
        question.ToTable("diagnostic_questions", ExamsDbOptions.Schema, table =>
        {
            table.HasCheckConstraint("ck_diagnostic_questions_stage", "stage IN ('primary', 'secondary')");
            table.HasCheckConstraint("ck_diagnostic_questions_level", "level IN ('prebasic', 'basic', 'intermediate', 'advanced')");
            table.HasCheckConstraint("ck_diagnostic_questions_kind", "kind IN ('multiple_choice', 'numeric')");
            table.HasCheckConstraint("ck_diagnostic_questions_position", "position >= 1");
            table.HasCheckConstraint("ck_diagnostic_questions_answer",
                "(kind = 'multiple_choice' AND correct_option IS NOT NULL) OR (kind = 'numeric' AND correct_number IS NOT NULL)");
        });
        question.HasKey(item => item.Id).HasName("pk_diagnostic_questions");
        question.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        question.Property(item => item.Stage).HasColumnName("stage").HasMaxLength(20).IsRequired();
        question.Property(item => item.Level).HasColumnName("level").HasMaxLength(20).IsRequired();
        question.Property(item => item.Topic).HasColumnName("topic").HasMaxLength(DiagnosticQuestion.MaxTopicLength).IsRequired();
        question.Property(item => item.Position).HasColumnName("position").IsRequired();
        question.Property(item => item.Kind).HasColumnName("kind").HasMaxLength(20)
            .HasConversion(value => ExerciseKinds.ToCode(value),
                value => value == ExerciseKinds.MultipleChoice ? ExerciseKind.MultipleChoice : ExerciseKind.Numeric)
            .IsRequired();
        question.Property(item => item.Prompt).HasColumnName("prompt").HasMaxLength(ExerciseRules.MaxPromptLength).IsRequired();
        question.Property(item => item.Options).HasColumnName("options").HasColumnType("text[]").IsRequired();
        question.Property(item => item.CorrectOption).HasColumnName("correct_option");
        question.Property(item => item.CorrectNumber).HasColumnName("correct_number").HasPrecision(18, 6);
        question.Property(item => item.Tolerance).HasColumnName("tolerance").HasPrecision(18, 6).IsRequired();
        question.Property(item => item.Explanation).HasColumnName("explanation").HasMaxLength(ExerciseRules.MaxExplanationLength);
        question.Property(item => item.IsArchived).HasColumnName("is_archived").IsRequired();
        question.Property(item => item.CreatedAt).HasColumnName("created_at").IsRequired();
        question.Property(item => item.UpdatedAt).HasColumnName("updated_at");
        // A stage's questions in order, and the per-stage count of active ones.
        question.HasIndex(item => new { item.Stage, item.IsArchived, item.Position }).HasDatabaseName("ix_diagnostic_questions_stage_active_position");

        var attempt = modelBuilder.Entity<DiagnosticAttempt>();
        attempt.ToTable("diagnostic_attempts", ExamsDbOptions.Schema, table =>
        {
            table.HasCheckConstraint("ck_diagnostic_attempts_stage", "stage IN ('primary', 'secondary')");
            table.HasCheckConstraint("ck_diagnostic_attempts_status", "status IN ('in_progress', 'completed')");
            table.HasCheckConstraint("ck_diagnostic_attempts_completed",
                "(status = 'in_progress' AND completed_at IS NULL AND suggested_level IS NULL) OR " +
                "(status = 'completed' AND completed_at IS NOT NULL AND suggested_level IS NOT NULL)");
            table.HasCheckConstraint("ck_diagnostic_attempts_confirmed",
                "(confirmed_level IS NULL) = (confirmed_by_user_id IS NULL) AND (confirmed_level IS NULL) = (confirmed_at IS NULL)");
        });
        attempt.HasKey(item => item.Id).HasName("pk_diagnostic_attempts");
        attempt.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        attempt.Property(item => item.StudentUserId).HasColumnName("student_user_id").IsRequired();
        attempt.Property(item => item.Stage).HasColumnName("stage").HasMaxLength(20).IsRequired();
        attempt.Property(item => item.QuestionIds).HasColumnName("question_ids").HasColumnType("uuid[]").IsRequired();
        attempt.Property(item => item.Status).HasColumnName("status").HasMaxLength(20)
            .HasConversion(value => value == DiagnosticStatus.Completed ? "completed" : "in_progress",
                value => value == "completed" ? DiagnosticStatus.Completed : DiagnosticStatus.InProgress)
            .IsRequired();
        attempt.Property(item => item.StartedAt).HasColumnName("started_at").IsRequired();
        attempt.Property(item => item.CompletedAt).HasColumnName("completed_at");
        attempt.Property(item => item.SuggestedLevel).HasColumnName("suggested_level").HasMaxLength(20);
        attempt.Property(item => item.ConfirmedLevel).HasColumnName("confirmed_level").HasMaxLength(20);
        attempt.Property(item => item.ConfirmedByUserId).HasColumnName("confirmed_by_user_id");
        attempt.Property(item => item.ConfirmedAt).HasColumnName("confirmed_at");
        // One diagnostic per student and stage: also stops a double start from two tabs.
        attempt.HasIndex(item => new { item.StudentUserId, item.Stage }).IsUnique().HasDatabaseName("ux_diagnostic_attempts_student_stage");
        // The teacher's list by status, newest first.
        attempt.HasIndex(item => new { item.Status, item.StartedAt }).HasDatabaseName("ix_diagnostic_attempts_status_started_at")
            .IsDescending(false, true);

        var answer = modelBuilder.Entity<DiagnosticAnswer>();
        answer.ToTable("diagnostic_answers", ExamsDbOptions.Schema, table =>
            table.HasCheckConstraint("ck_diagnostic_answers_skipped", "answer IS NOT NULL OR NOT is_correct"));
        answer.HasKey(item => item.Id).HasName("pk_diagnostic_answers");
        answer.Property(item => item.Id).HasColumnName("id").ValueGeneratedNever();
        answer.Property(item => item.AttemptId).HasColumnName("attempt_id").IsRequired();
        answer.Property(item => item.QuestionId).HasColumnName("question_id").IsRequired();
        answer.Property(item => item.Topic).HasColumnName("topic").HasMaxLength(DiagnosticQuestion.MaxTopicLength).IsRequired();
        answer.Property(item => item.Level).HasColumnName("level").HasMaxLength(20).IsRequired();
        answer.Property(item => item.Answer).HasColumnName("answer").HasMaxLength(ExerciseRules.MaxAnswerLength);
        answer.Property(item => item.IsCorrect).HasColumnName("is_correct").IsRequired();
        answer.Property(item => item.AnsweredAt).HasColumnName("answered_at").IsRequired();
        answer.HasOne<DiagnosticAttempt>().WithMany().HasForeignKey(item => item.AttemptId)
            .HasConstraintName("fk_diagnostic_answers_diagnostic_attempts_attempt_id").OnDelete(DeleteBehavior.Cascade);
        answer.HasOne<DiagnosticQuestion>().WithMany().HasForeignKey(item => item.QuestionId)
            .HasConstraintName("fk_diagnostic_answers_diagnostic_questions_question_id").OnDelete(DeleteBehavior.Restrict);
        // One answer per question and attempt; also reads an attempt's answers and counts them.
        answer.HasIndex(item => new { item.AttemptId, item.QuestionId }).IsUnique().HasDatabaseName("ux_diagnostic_answers_attempt_question");
        answer.HasIndex(item => item.QuestionId).HasDatabaseName("ix_diagnostic_answers_question");
    }
}

// Same conventions as the other modules: snake_case migrations history in the module schema.
#pragma warning disable EF1001
public sealed class SnakeCaseNpgsqlHistoryRepository(HistoryRepositoryDependencies dependencies)
    : NpgsqlHistoryRepository(dependencies)
{
    protected override void ConfigureTable(EntityTypeBuilder<HistoryRow> history)
    {
        base.ConfigureTable(history);
        history.Property(row => row.MigrationId).HasColumnName("migration_id");
        history.Property(row => row.ProductVersion).HasColumnName("product_version");
        history.HasKey(row => row.MigrationId).HasName("pk___ef_migrations_history");
    }
}
#pragma warning restore EF1001

public static class ExamsDbOptions
{
    public const string Schema = "exams";

    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema))
            .ReplaceService<IHistoryRepository, SnakeCaseNpgsqlHistoryRepository>();
}

internal sealed class ExamsDbContextFactory : IDesignTimeDbContextFactory<ExamsDbContext>
{
    public ExamsDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings__DefaultConnection must be provided externally.");

        var options = new DbContextOptionsBuilder<ExamsDbContext>();
        ExamsDbOptions.Configure(options, connectionString);
        return new ExamsDbContext(options.Options);
    }
}
