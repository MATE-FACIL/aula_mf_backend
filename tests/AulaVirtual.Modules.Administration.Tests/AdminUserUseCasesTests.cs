using AulaVirtual.Modules.Administration.Api.Controllers;
using AulaVirtual.Modules.Administration.Application.Users;
using AulaVirtual.Modules.Auth.Application.Accounts.ChangeStatus;
using AulaVirtual.Modules.Auth.Application.Accounts.CreateAccount;
using AulaVirtual.Modules.Auth.Domain.Accounts;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Users.Application.Abstractions;
using AulaVirtual.Modules.Users.Application.Consent;
using Microsoft.AspNetCore.Authorization;

namespace AulaVirtual.Modules.Administration.Tests;

public sealed class AdminUserUseCasesTests
{
    private readonly FakeBackOffice backOffice = new();

    private AdminUserUseCases UseCases() => new(
        backOffice,
        backOffice,
        backOffice,
        backOffice,
        backOffice,
        new AuthorizationUseCases(backOffice, backOffice, backOffice),
        new CreateStudentAccountHandler(backOffice, backOffice, backOffice, backOffice,
            new CreateStudentAccountCommandValidator(), TimeProvider.System),
        new CreateAdministratorAccountHandler(backOffice, backOffice, backOffice, backOffice,
            new CreateAdministratorAccountCommandValidator(), TimeProvider.System),
        new ChangeAccountStatusHandler(backOffice, TimeProvider.System),
        TimeProvider.System);

    [Fact]
    public void Controller_RequiresUsersManagePermission()
    {
        var attribute = Assert.Single(typeof(AdminUsersController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(Permissions.UsersManage, attribute.Policy);
    }

    [Fact]
    public async Task List_CombinesRoleAndStatusFilters_AndEnrichesEachRow()
    {
        var activeStudent = backOffice.AddPerson("Ana", "Quispe", Roles.Student);
        backOffice.AddPerson("Luis", "Rojas", Roles.Student, AccountStatus.Inactive);
        backOffice.AddPerson("Rosa", "Mamani", Roles.Guardian);

        var result = await UseCases().ListAsync(
            new AdminUserListQuery(null, Roles.Student, AdminAccountStatus.Active), default);

        Assert.Equal(AdminResultStatus.Ok, result.Status);
        var row = Assert.Single(result.Value!.Items);
        Assert.Equal(activeStudent, row.Id);
        Assert.Equal([Roles.Student], row.Roles);
        Assert.Equal(AdminAccountStatus.Active, row.Status);
        Assert.Equal("ana@example.com", row.LoginIdentifier);
        Assert.Equal(1, result.Value.Total);
    }

    [Theory]
    [InlineData("superuser", null)]
    [InlineData(null, "deleted")]
    public async Task List_RejectsUnknownFilters(string? role, string? status)
    {
        var result = await UseCases().ListAsync(new AdminUserListQuery(null, role, status), default);
        Assert.Equal(AdminResultStatus.Invalid, result.Status);
    }

    [Fact]
    public async Task List_ClampsPaging()
    {
        for (var i = 0; i < 3; i++) backOffice.AddPerson($"Alumno{i}", "Prueba", Roles.Student);

        var result = await UseCases().ListAsync(new AdminUserListQuery(null, null, null, Page: 0, PageSize: 1000),
            default);

        Assert.Equal(1, result.Value!.Page);
        Assert.Equal(AdminUserListQuery.MaxPageSize, result.Value.PageSize);
        Assert.Equal(3, result.Value.Items.Count);
    }

    [Fact]
    public async Task Summary_CountsNewUsersAndRoles()
    {
        backOffice.AddPerson("Antiguo", "Alumno", Roles.Student, createdAt: DateTimeOffset.UtcNow.AddDays(-30));
        backOffice.AddPerson("Nuevo", "Alumno", Roles.Student, createdAt: DateTimeOffset.UtcNow.AddDays(-2));
        backOffice.AddPerson("Nueva", "Madre", Roles.Guardian, createdAt: DateTimeOffset.UtcNow.AddDays(-1));

        var summary = await UseCases().GetSummaryAsync(default);

        Assert.Equal(3, summary.TotalUsers);
        Assert.Equal(2, summary.NewUsersLast7Days);
        Assert.Equal(2, summary.UsersByRole[Roles.Student]);
        Assert.Equal(1, summary.UsersByRole[Roles.Guardian]);
        Assert.Equal(3, summary.RecentUsers.Count);
        Assert.Equal("Nueva Madre", summary.RecentUsers[0].FullName);
    }

    [Fact]
    public async Task CreateStudent_AssignsStudentRole_AndLinksGuardian()
    {
        var guardian = backOffice.AddPerson("Rosa", "Mamani", Roles.Guardian);

        var result = await UseCases().CreateStudentAsync(
            new CreateStudentInput("Ana", "Quispe", null, null, "DNI", "77340061", guardian), default);

        Assert.Equal(AdminResultStatus.Ok, result.Status);
        Assert.Equal("Q77340061", result.Value!.LoginIdentifier);
        Assert.Equal("Generated-Password-1", result.Value.InitialPassword);
        Assert.Contains(Roles.Student,
            (await backOffice.GetRoleCodesByUserIdsAsync([result.Value.UserId], default))[result.Value.UserId]);
        Assert.Contains((guardian, result.Value.UserId), backOffice.Links);
    }

    [Fact]
    public async Task CreateStudent_WithUnknownGuardian_CreatesNothing()
    {
        var notAGuardian = backOffice.AddPerson("Luis", "Rojas", Roles.Student);
        var before = backOffice.People.Count;

        var result = await UseCases().CreateStudentAsync(
            new CreateStudentInput("Ana", "Quispe", null, null, "DNI", "77340061", notAGuardian), default);

        Assert.Equal(AdminResultStatus.Invalid, result.Status);
        Assert.Equal(before, backOffice.People.Count);
    }

    [Fact]
    public async Task CreateStudent_WithDuplicateDocument_IsAConflict()
    {
        await UseCases().CreateStudentAsync(
            new CreateStudentInput("Ana", "Quispe", null, null, "DNI", "77340061", null), default);

        var result = await UseCases().CreateStudentAsync(
            new CreateStudentInput("Ana María", "Torres", null, null, "DNI", "77340061", null), default);

        Assert.Equal(AdminResultStatus.Conflict, result.Status);
    }

    [Fact]
    public async Task CreateStudent_WithMissingData_ReturnsFieldErrors()
    {
        var result = await UseCases().CreateStudentAsync(
            new CreateStudentInput("", "Quispe", null, null, "DNI", "77340061", null), default);

        Assert.Equal(AdminResultStatus.Invalid, result.Status);
        Assert.NotNull(result.Errors);
        Assert.Empty(backOffice.People);
    }

    [Fact]
    public async Task CreateGuardian_UsesEmailLogin_AndGuardianRole()
    {
        var result = await UseCases().CreateGuardianAsync(
            new CreateGuardianInput("Rosa", "Mamani", null, "rosa@example.com", null, null), default);

        Assert.Equal(AdminResultStatus.Ok, result.Status);
        Assert.Equal("rosa@example.com", result.Value!.LoginIdentifier);
        Assert.Equal([Roles.Guardian],
            (await backOffice.GetRoleCodesByUserIdsAsync([result.Value.UserId], default))[result.Value.UserId]);
    }

    [Fact]
    public async Task Link_RequiresAGuardianAndAStudent()
    {
        var guardian = backOffice.AddPerson("Rosa", "Mamani", Roles.Guardian);
        var student = backOffice.AddPerson("Ana", "Quispe", Roles.Student);
        var useCases = UseCases();

        Assert.Equal(AdminResultStatus.Invalid, (await useCases.LinkAsync(student, guardian, default)).Status);
        Assert.Empty(backOffice.Links);

        var linked = await useCases.LinkAsync(guardian, student, default);
        var again = await useCases.LinkAsync(guardian, student, default);

        Assert.True(linked.Value);
        Assert.False(again.Value);
        Assert.Single(backOffice.Links);
    }

    [Fact]
    public async Task Detail_ListsLinkedStudentsAndGuardians()
    {
        var guardian = backOffice.AddPerson("Rosa", "Mamani", Roles.Guardian);
        var student = backOffice.AddPerson("Ana", "Quispe", Roles.Student);
        var useCases = UseCases();
        await useCases.LinkAsync(guardian, student, default);

        var guardianDetail = await useCases.GetAsync(guardian, default);
        var studentDetail = await useCases.GetAsync(student, default);

        Assert.Equal(student, Assert.Single(guardianDetail!.Students).Id);
        Assert.Empty(guardianDetail.Guardians);
        Assert.Equal(guardian, Assert.Single(studentDetail!.Guardians).Id);
        Assert.Null(await useCases.GetAsync(Guid.NewGuid(), default));
    }

    [Fact]
    public async Task Detail_OfAStudent_ShowsWhereTheParentsConsentStands()
    {
        var version = ConsentTexts.Current.Version;
        var mother = backOffice.AddPerson("Rosa", "Mamani", Roles.Guardian);
        var student = backOffice.AddPerson("Ana", "Quispe", Roles.Student);
        var useCases = UseCases();

        // Nobody can give the consent yet.
        var alone = (await useCases.GetAsync(student, default))!.Consent!;
        Assert.Equal((AdminConsentStatus.NoGuardian, version, null), (alone.Status, alone.CurrentVersion, alone.AcceptedAt));

        // A guardian is linked but has not accepted: pending. Accepting another (older) text does not count.
        await useCases.LinkAsync(mother, student, default);
        backOffice.Consents.Add(new ConsentRecord(student, mother, "2025-01-old", DateTimeOffset.UtcNow.AddDays(-30)));
        Assert.Equal(AdminConsentStatus.Pending, (await useCases.GetAsync(student, default))!.Consent!.Status);

        // Accepted: with who and when.
        var at = DateTimeOffset.UtcNow.AddHours(-2);
        await backOffice.AcceptAsync(mother, [student], version, at, default);
        var accepted = (await useCases.GetAsync(student, default))!.Consent!;
        Assert.Equal((AdminConsentStatus.Accepted, "Rosa Mamani", at, ConsentTexts.Current.IsProvisional),
            (accepted.Status, accepted.AcceptedByName, accepted.AcceptedAt, accepted.IsProvisional));

        // An acceptance only counts while that guardian is still linked to the student.
        await useCases.UnlinkAsync(mother, student, default);
        Assert.Equal(AdminConsentStatus.NoGuardian, (await useCases.GetAsync(student, default))!.Consent!.Status);
        var father = backOffice.AddPerson("Luis", "Quispe", Roles.Guardian);
        await useCases.LinkAsync(father, student, default);
        Assert.Equal(AdminConsentStatus.Pending, (await useCases.GetAsync(student, default))!.Consent!.Status);
    }

    [Fact]
    public async Task Detail_OfAGuardianOrStaff_HasNoConsent()
    {
        var guardian = backOffice.AddPerson("Rosa", "Mamani", Roles.Guardian);
        var admin = backOffice.AddPerson("Marta", "Gómez", Roles.Administrator);
        var useCases = UseCases();

        Assert.Null((await useCases.GetAsync(guardian, default))!.Consent);
        Assert.Null((await useCases.GetAsync(admin, default))!.Consent);
    }

    [Fact]
    public async Task SetActive_DeactivatesOthers_ButNeverTheActor()
    {
        var admin = backOffice.AddPerson("Admin", "Aula", Roles.Administrator);
        var student = backOffice.AddPerson("Ana", "Quispe", Roles.Student);
        var useCases = UseCases();

        Assert.Equal(AdminResultStatus.Invalid, (await useCases.SetActiveAsync(admin, admin, false, default)).Status);
        Assert.True((await useCases.SetActiveAsync(admin, student, false, default)).Value);
        Assert.False((await useCases.SetActiveAsync(admin, student, false, default)).Value);
        Assert.Equal(AccountStatus.Inactive, backOffice.Accounts.Single(account => account.UserId == student).Status);

        var withoutAccount = backOffice.AddPerson("Sin", "Cuenta", Roles.Student, status: null);
        Assert.Equal(AdminResultStatus.NotFound,
            (await useCases.SetActiveAsync(admin, withoutAccount, true, default)).Status);
    }
}
