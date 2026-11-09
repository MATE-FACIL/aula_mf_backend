using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations.Internal;

namespace AulaVirtual.Modules.Attendance.Infrastructure.Persistence;

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

public static class AttendanceDbOptions
{
    public const string Schema = "attendance";

    public static DbContextOptionsBuilder Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(connectionString,
                npgsql => npgsql.MigrationsHistoryTable("__ef_migrations_history", Schema))
            .ReplaceService<IHistoryRepository, SnakeCaseNpgsqlHistoryRepository>();
}

internal sealed class AttendanceDbContextFactory : IDesignTimeDbContextFactory<AttendanceDbContext>
{
    public AttendanceDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException("ConnectionStrings__DefaultConnection must be provided externally.");

        var options = new DbContextOptionsBuilder<AttendanceDbContext>();
        AttendanceDbOptions.Configure(options, connectionString);
        return new AttendanceDbContext(options.Options);
    }
}
