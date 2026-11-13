using AulaVirtual.Modules.Users.Domain.Guardianship;
using AulaVirtual.Modules.Users.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AulaVirtual.Modules.Users.Infrastructure.Persistence.Configurations;

internal sealed class GuardianConsentConfiguration : IEntityTypeConfiguration<GuardianConsent>
{
    public void Configure(EntityTypeBuilder<GuardianConsent> builder)
    {
        builder.ToTable("guardian_consents", "users", table => table.HasCheckConstraint(
            "ck_guardian_consents_distinct_users", "guardian_user_id <> student_user_id"));

        builder.HasKey(consent => consent.Id).HasName("pk_guardian_consents");

        builder.Property(consent => consent.Id).HasColumnName("id").ValueGeneratedNever();
        builder.Property(consent => consent.GuardianUserId).HasColumnName("guardian_user_id").IsRequired();
        builder.Property(consent => consent.StudentUserId).HasColumnName("student_user_id").IsRequired();
        builder.Property(consent => consent.TermsVersion).HasColumnName("terms_version")
            .HasMaxLength(GuardianConsent.MaxVersionLength).IsRequired();
        builder.Property(consent => consent.AcceptedAt).HasColumnName("accepted_at").IsRequired();

        // One acceptance per guardian, student and version: repeating it changes nothing (ON CONFLICT DO NOTHING).
        builder.HasIndex(consent => new { consent.GuardianUserId, consent.StudentUserId, consent.TermsVersion })
            .IsUnique()
            .HasDatabaseName("ux_guardian_consents_guardian_student_version");

        // «Where does this student stand with this version»: the status of the family panel and of the admin file.
        builder.HasIndex(consent => new { consent.StudentUserId, consent.TermsVersion })
            .HasDatabaseName("ix_guardian_consents_student_version");

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(consent => consent.GuardianUserId)
            .HasConstraintName("fk_guardian_consents_users_guardian_user_id")
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<User>()
            .WithMany()
            .HasForeignKey(consent => consent.StudentUserId)
            .HasConstraintName("fk_guardian_consents_users_student_user_id")
            .OnDelete(DeleteBehavior.Cascade);
    }
}
