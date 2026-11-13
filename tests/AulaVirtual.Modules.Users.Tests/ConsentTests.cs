using AulaVirtual.Modules.Users.Api.Controllers;
using AulaVirtual.Modules.Users.Application.Abstractions;
using AulaVirtual.Modules.Users.Application.Consent;
using AulaVirtual.Modules.Users.Domain.Guardianship;
using AulaVirtual.Modules.Users.Infrastructure.Persistence;
using AulaVirtual.Tests.Shared;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;

namespace AulaVirtual.Modules.Users.Tests;

public sealed class ConsentTextTests
{
    [Fact]
    public void TheCurrentText_HasAVersionThatFitsTheTable_AndSomethingToRead()
    {
        var text = ConsentTexts.Current;

        Assert.False(string.IsNullOrWhiteSpace(text.Version));
        Assert.InRange(text.Version.Length, 1, GuardianConsent.MaxVersionLength);
        Assert.False(string.IsNullOrWhiteSpace(text.Title));
        Assert.All(text.Sections, section =>
        {
            Assert.False(string.IsNullOrWhiteSpace(section.Heading));
            Assert.NotEmpty(section.Paragraphs);
            Assert.All(section.Paragraphs, paragraph => Assert.False(string.IsNullOrWhiteSpace(paragraph)));
        });
        Assert.True(text.Sections.Count >= 3);
    }
}

public sealed class GuardianConsentUseCaseTests
{
    private readonly Fakes fakes = new();
    private readonly string version = ConsentTexts.Current.Version;
    private readonly Guid mother;
    private readonly Guid father;
    private readonly Guid ana;
    private readonly Guid beto;

    public GuardianConsentUseCaseTests()
    {
        mother = fakes.AddPerson("Rosa", "Mamani");
        father = fakes.AddPerson("Luis", "Quispe");
        ana = fakes.AddPerson("Ana", "Quispe");
        beto = fakes.AddPerson("Beto", "Quispe");
        fakes.Link(mother, ana);
        fakes.Link(mother, beto);
        fakes.Link(father, ana);
    }

    private GuardianConsentUseCases UseCases() => new(fakes, fakes, fakes, TimeProvider.System);

    [Fact]
    public void TheController_NeedsASignedInUser_AndStartsFromTheirOwnLinks()
    {
        Assert.Single(typeof(FamilyConsentController).GetCustomAttributes(typeof(AuthorizeAttribute), true));
        Assert.DoesNotContain(typeof(FamilyConsentController).GetMethods(), method =>
            method.GetParameters().Any(parameter => parameter.Name is "guardianUserId" or "guardianId"));
    }

    [Fact]
    public async Task TheCurrentTextAndEachChild_StartPending_InAlphabeticalOrder()
    {
        var consent = await UseCases().GetAsync(mother, default);

        Assert.Equal(version, consent.Text.Version);
        Assert.Equal(["Ana Quispe", "Beto Quispe"], consent.Children.Select(child => child.FullName));
        Assert.All(consent.Children, child => Assert.Equal(ConsentStatuses.Pending, child.Status));
        Assert.Equal(2, consent.Pending);
    }

    [Fact]
    public async Task Accepting_RecordsTheDateAndWho_AndLeavesTheOtherChildPending()
    {
        var result = await UseCases().AcceptAsync(mother, version, [ana], default);

        Assert.Equal(ConsentOutcomeStatus.Ok, result.Status);
        var children = result.Value!.Children.ToDictionary(child => child.StudentUserId);
        Assert.Equal((ConsentStatuses.Accepted, "Rosa Mamani"), (children[ana].Status, children[ana].AcceptedByName));
        Assert.InRange(children[ana].AcceptedAt!.Value, DateTimeOffset.UtcNow.AddMinutes(-1), DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(ConsentStatuses.Pending, children[beto].Status);
        Assert.Equal(1, result.Value.Pending);
    }

    [Fact]
    public async Task AcceptingForSeveralChildren_IsOneCall_AndRepeatingItChangesNothing()
    {
        var first = await UseCases().AcceptAsync(mother, version, [ana, beto, ana], default);
        Assert.Equal(1, fakes.AcceptCalls);

        var again = await UseCases().AcceptAsync(mother, version, [ana, beto], default);

        Assert.Equal(0, first.Value!.Pending);
        Assert.Equal(0, again.Value!.Pending);
        Assert.Equal(2, fakes.Consents.Count);
    }

    [Fact]
    public async Task OneParentIsEnough_ForTheChildTheyShare()
    {
        await UseCases().AcceptAsync(father, version, [ana], default);

        var consent = await UseCases().GetAsync(mother, default);

        var child = consent.Children.Single(item => item.StudentUserId == ana);
        Assert.Equal((ConsentStatuses.Accepted, "Luis Quispe"), (child.Status, child.AcceptedByName));
    }

    [Fact]
    public async Task AllOrNone_ANotOwnChildMeansNothingIsSaved()
    {
        var stranger = fakes.AddPerson("Carla", "Ríos");

        var result = await UseCases().AcceptAsync(mother, version, [ana, stranger], default);

        Assert.Equal(ConsentOutcomeStatus.NotFound, result.Status);
        Assert.Empty(fakes.Consents);
        Assert.Equal(ConsentOutcomeStatus.NotFound, (await UseCases().AcceptAsync(father, version, [beto], default)).Status);
    }

    [Fact]
    public async Task ATextThatChangedWhileReading_IsRefused_WithoutSavingAnything()
    {
        var result = await UseCases().AcceptAsync(mother, "2020-otra-version", [ana], default);

        Assert.Equal(ConsentOutcomeStatus.Conflict, result.Status);
        Assert.Contains("Recarga", result.Detail);
        Assert.Empty(fakes.Consents);
        Assert.Equal(ConsentOutcomeStatus.Conflict, (await UseCases().AcceptAsync(mother, null, [ana], default)).Status);
    }

    [Fact]
    public async Task ItNeedsAtLeastOneChild_AndNotTooMany()
    {
        Assert.Equal(ConsentOutcomeStatus.Invalid, (await UseCases().AcceptAsync(mother, version, [], default)).Status);
        Assert.Equal(ConsentOutcomeStatus.Invalid, (await UseCases().AcceptAsync(mother, version, null, default)).Status);
        var many = Enumerable.Range(0, GuardianConsentUseCases.MaxChildrenPerRequest + 1).Select(_ => Guid.NewGuid()).ToArray();
        Assert.Equal(ConsentOutcomeStatus.Invalid, (await UseCases().AcceptAsync(mother, version, many, default)).Status);
    }

    [Fact]
    public async Task ANewTextAsksAgain_AndTheOldAcceptanceStaysAsEvidence()
    {
        fakes.Consents.Add(new ConsentRecord(ana, mother, "2025-01-old", DateTimeOffset.UtcNow.AddDays(-30)));

        var before = await UseCases().GetAsync(mother, default);
        await UseCases().AcceptAsync(mother, version, [ana], default);

        Assert.Equal(ConsentStatuses.Pending, before.Children.Single(child => child.StudentUserId == ana).Status);
        Assert.Equal(2, fakes.Consents.Count(consent => consent.StudentUserId == ana));
    }

    [Fact]
    public async Task AnAcceptance_OnlyCountsWhileThatGuardianIsStillLinked()
    {
        await UseCases().AcceptAsync(mother, version, [beto], default);
        fakes.Unlink(mother, beto);

        Assert.Empty((await UseCases().GetAsync(mother, default)).Children.Where(child => child.StudentUserId == beto));
        fakes.Link(father, beto);

        var viewed = await UseCases().GetAsync(father, default);
        Assert.Equal(ConsentStatuses.Pending, viewed.Children.Single(child => child.StudentUserId == beto).Status);
    }

    [Fact]
    public async Task AnAccountWithoutChildren_GetsTheTextAndAnEmptyList_WithoutFurtherQueries()
    {
        var consent = await UseCases().GetAsync(Guid.NewGuid(), default);

        Assert.NotEmpty(consent.Text.Sections);
        Assert.Empty(consent.Children);
        Assert.Equal(0, consent.Pending);
        // Only the lookup of its own links: no acceptances and no people are read.
        Assert.Equal(1, fakes.Queries);
    }

    [Fact]
    public async Task EveryReadHasAFixedNumberOfQueries_WhateverTheNumberOfChildren()
    {
        for (var i = 0; i < 12; i++) fakes.Link(mother, fakes.AddPerson($"Hijo{i}", "Quispe"));

        await UseCases().GetAsync(mother, default);

        Assert.Equal(3, fakes.Queries);
    }

    private sealed class Fakes : IGuardianLinks, IGuardianConsents, IUserDirectory
    {
        private readonly List<UserSummary> people = [];
        private readonly HashSet<(Guid Guardian, Guid Student)> links = [];

        public List<ConsentRecord> Consents { get; } = [];
        public int Queries { get; private set; }
        public int AcceptCalls { get; private set; }

        public Guid AddPerson(string given, string surname)
        {
            var id = Guid.NewGuid();
            people.Add(new UserSummary(id, given, surname, null, null, "DNI", "70000000", DateTimeOffset.UtcNow, 0));
            return id;
        }

        public void Link(Guid guardian, Guid student) => links.Add((guardian, student));

        public void Unlink(Guid guardian, Guid student) => links.Remove((guardian, student));

        public Task<IReadOnlyList<Guid>> GetStudentIdsAsync(Guid guardianUserId, CancellationToken cancellationToken)
        {
            Queries++;
            return Task.FromResult<IReadOnlyList<Guid>>(links.Where(link => link.Guardian == guardianUserId).Select(link => link.Student).ToList());
        }

        public Task<IReadOnlyList<ConsentRecord>> GetAcceptedAsync(IReadOnlyCollection<Guid> studentUserIds, string termsVersion,
            CancellationToken cancellationToken)
        {
            Queries++;
            return Task.FromResult<IReadOnlyList<ConsentRecord>>(Consents
                .Where(consent => studentUserIds.Contains(consent.StudentUserId) && consent.TermsVersion == termsVersion &&
                    links.Contains((consent.GuardianUserId, consent.StudentUserId)))
                .ToList());
        }

        // The same rules as the one-statement insert: only the guardian's own links, and what is already there is skipped.
        public Task<int> AcceptAsync(Guid guardianUserId, IReadOnlyCollection<Guid> studentUserIds, string termsVersion,
            DateTimeOffset acceptedAt, CancellationToken cancellationToken)
        {
            AcceptCalls++;
            var added = 0;
            foreach (var studentId in studentUserIds.Where(id => links.Contains((guardianUserId, id))))
            {
                if (Consents.Any(consent => consent.GuardianUserId == guardianUserId && consent.StudentUserId == studentId &&
                    consent.TermsVersion == termsVersion)) continue;
                Consents.Add(new ConsentRecord(studentId, guardianUserId, termsVersion, acceptedAt));
                added++;
            }

            return Task.FromResult(added);
        }

        public Task<IReadOnlyList<UserSummary>> GetManyAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
        {
            Queries++;
            return Task.FromResult<IReadOnlyList<UserSummary>>(people.Where(person => userIds.Contains(person.Id)).ToList());
        }

        public Task<bool> LinkAsync(Guid guardianUserId, Guid studentUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> UnlinkAsync(Guid guardianUserId, Guid studentUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<Guid>> GetGuardianIdsAsync(Guid studentUserId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UserSearchPage> SearchAsync(UserSearchQuery query, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<UserSummary?> GetAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<int> CountAsync(DateTimeOffset? createdSince, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}

// The consent queries translate to PostgreSQL in one statement each.
public sealed class ConsentQueryTests
{
    private readonly SqlCapture capture = new();

    private GuardianConsentStore Store() => new(new UsersDbContext(new DbContextOptionsBuilder<UsersDbContext>()
        .UseNpgsql("Host=localhost;Database=model_only;Username=model").AddInterceptors(capture).Options));

    [Fact]
    public void TheModel_KeepsOneAcceptancePerGuardianStudentAndVersion()
    {
        using var context = new UsersDbContext(new DbContextOptionsBuilder<UsersDbContext>()
            .UseNpgsql("Host=localhost;Database=model_only;Username=model").Options);
        var entity = context.Model.FindEntityType(typeof(GuardianConsent))!;

        Assert.Equal(("users", "guardian_consents"), (entity.GetSchema(), entity.GetTableName()));
        var unique = Assert.Single(entity.GetIndexes(), index => index.IsUnique);
        Assert.Equal([nameof(GuardianConsent.GuardianUserId), nameof(GuardianConsent.StudentUserId), nameof(GuardianConsent.TermsVersion)],
            unique.Properties.Select(property => property.Name));
        Assert.Equal(GuardianConsent.MaxVersionLength, entity.FindProperty(nameof(GuardianConsent.TermsVersion))!.GetMaxLength());
    }

    [Fact]
    public async Task Accepted_IsOneQuery_JoinedWithTheLinks()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Store().GetAcceptedAsync([Guid.NewGuid(), Guid.NewGuid()], "v1", default));

        Assert.Contains("guardian_consents", sql);
        Assert.Contains("guardian_links", sql);
        Assert.Contains("terms_version", sql);
    }

    [Fact]
    public async Task Accepting_IsOneStatement_ThatReadsTheLinksAndSkipsWhatIsThere()
    {
        var sql = await SqlCapture.RunAsync(capture, () => Store().AcceptAsync(Guid.NewGuid(), [Guid.NewGuid(), Guid.NewGuid()], "v1",
            DateTimeOffset.UtcNow, default));

        Assert.Contains("INSERT INTO users.guardian_consents", sql);
        Assert.Contains("FROM users.guardian_links", sql);
        Assert.Contains("ON CONFLICT (guardian_user_id, student_user_id, terms_version) DO NOTHING", sql);
        Assert.Contains("ANY(", sql);
    }
}
