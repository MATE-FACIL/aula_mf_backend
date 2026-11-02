using AulaVirtual.Modules.Users.Domain.Guardianship;
using AulaVirtual.Modules.Users.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AulaVirtual.Modules.Users.Infrastructure.Persistence.Configurations;

internal sealed class GuardianLinkConfiguration : IEntityTypeConfiguration<GuardianLink>
{
    public void Configure(EntityTypeBuilder<GuardianLink> builder)
    {
        builder.ToTable("guardian_links", "users", table => table.HasCheckConstraint(
            "ck_guardian_links_distinct_users", "guardian_user_id <> student_user_id"));

        builder.HasKey(link => new { link.GuardianUserId, link.StudentUserId })
            .HasName("pk_guardian_links");

        builder.Property(link => link.GuardianUserId)
            .HasColumnName("guardian_user_id");

        builder.Property(link => link.StudentUserId)
            .HasColumnName("student_user_id");

        builder.Property(link => link.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        // The primary key already covers lookups by guardian; this one serves «guardians of a student».
        builder.HasIndex(link => link.StudentUserId)
            .HasDatabaseName("ix_guardian_links_student_user_id");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(link => link.GuardianUserId)
            .HasConstraintName("fk_guardian_links_users_guardian_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(link => link.StudentUserId)
            .HasConstraintName("fk_guardian_links_users_student_user_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
