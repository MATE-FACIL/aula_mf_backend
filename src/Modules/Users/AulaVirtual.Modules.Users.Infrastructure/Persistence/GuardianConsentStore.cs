using AulaVirtual.Modules.Users.Application.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Users.Infrastructure.Persistence;

public sealed class GuardianConsentStore(UsersDbContext dbContext) : IGuardianConsents
{
    public async Task<IReadOnlyList<ConsentRecord>> GetAcceptedAsync(IReadOnlyCollection<Guid> studentUserIds,
        string termsVersion, CancellationToken cancellationToken)
    {
        if (studentUserIds.Count == 0) return [];
        var ids = studentUserIds.ToArray();

        // Joined with the links: an acceptance only counts while that guardian is still linked to the student.
        // Served by ix_guardian_consents_student_version and the primary key of the links.
        return await (
                from consent in dbContext.GuardianConsents.AsNoTracking()
                join link in dbContext.GuardianLinks.AsNoTracking()
                    on new { consent.GuardianUserId, consent.StudentUserId }
                    equals new { link.GuardianUserId, link.StudentUserId }
                where ids.Contains(consent.StudentUserId) && consent.TermsVersion == termsVersion
                select new ConsentRecord(consent.StudentUserId, consent.GuardianUserId, consent.TermsVersion, consent.AcceptedAt))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> AcceptAsync(Guid guardianUserId, IReadOnlyCollection<Guid> studentUserIds, string termsVersion,
        DateTimeOffset acceptedAt, CancellationToken cancellationToken)
    {
        if (studentUserIds.Count == 0) return 0;
        var ids = studentUserIds.ToArray();
        var at = acceptedAt.ToUniversalTime();

        // ONE statement for any number of children, and atomic: it reads the guardian's links itself (so nobody can
        // accept for a student who is not theirs, even if a link is removed a moment ago) and skips what is already
        // accepted (so two requests at once, or a repeated click, cannot duplicate or fail).
        return await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
             INSERT INTO users.guardian_consents (id, guardian_user_id, student_user_id, terms_version, accepted_at)
             SELECT gen_random_uuid(), link.guardian_user_id, link.student_user_id, {termsVersion}, {at}
             FROM users.guardian_links AS link
             WHERE link.guardian_user_id = {guardianUserId} AND link.student_user_id = ANY({ids})
             ON CONFLICT (guardian_user_id, student_user_id, terms_version) DO NOTHING
             """,
            cancellationToken);
    }
}
