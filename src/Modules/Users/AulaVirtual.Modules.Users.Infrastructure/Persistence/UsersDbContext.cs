using AulaVirtual.Modules.Users.Domain.Guardianship;
using AulaVirtual.Modules.Users.Domain.Users;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Users.Infrastructure.Persistence;

public sealed class UsersDbContext(DbContextOptions<UsersDbContext> options)
    : DbContext(options)
{
    public DbSet<User> Users => Set<User>();

    public DbSet<GuardianLink> GuardianLinks => Set<GuardianLink>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("users");
        // Accent-insensitive admin search ("lucia" finds "Lucía"). A trusted extension since PostgreSQL 13.
        modelBuilder.HasPostgresExtension("unaccent");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(UsersDbContext).Assembly);
    }
}
