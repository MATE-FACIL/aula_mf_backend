using AulaVirtual.Modules.Users.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AulaVirtual.Modules.Users.Infrastructure.Persistence.Configurations;

internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users", "users");

        builder.HasKey(user => user.Id)
            .HasName("pk_users");

        builder.Property(user => user.Id)
            .HasColumnName("id")
            .ValueGeneratedNever();

        builder.Property(user => user.GivenNames)
            .HasColumnName("given_names")
            .HasMaxLength(150)
            .IsRequired();

        builder.Property(user => user.FirstSurname)
            .HasColumnName("first_surname")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(user => user.SecondSurname)
            .HasColumnName("second_surname")
            .HasMaxLength(100);

        builder.Property(user => user.Email)
            .HasColumnName("email")
            .HasMaxLength(254);

        builder.Property(user => user.DocumentType)
            .HasColumnName("document_type")
            .HasMaxLength(32);

        builder.Property(user => user.DocumentNumber)
            .HasColumnName("document_number")
            .HasMaxLength(50);

        builder.Property(user => user.CreatedAt)
            .HasColumnName("created_at")
            .IsRequired();

        builder.Property(user => user.UpdatedAt)
            .HasColumnName("updated_at");

        // Admin listings are ordered by most recent first.
        builder.HasIndex(user => user.CreatedAt)
            .HasDatabaseName("ix_users_created_at");

        builder.HasIndex(user => user.DocumentNumber)
            .HasDatabaseName("ix_users_document_number")
            .IsUnique()
            .HasFilter("document_number IS NOT NULL");
    }
}
