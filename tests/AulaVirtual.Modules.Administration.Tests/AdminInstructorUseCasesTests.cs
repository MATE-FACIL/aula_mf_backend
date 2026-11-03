using AulaVirtual.Modules.Administration.Api.Controllers;
using AulaVirtual.Modules.Administration.Application.Users;
using AulaVirtual.Modules.Auth.Application.Accounts.CreateAccount;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Courses.Application.Instructors;
using AulaVirtual.Modules.Courses.Domain.Instructors;
using Microsoft.AspNetCore.Authorization;

namespace AulaVirtual.Modules.Administration.Tests;

public sealed class AdminInstructorUseCasesTests
{
    private readonly FakeBackOffice backOffice = new();
    private readonly FakeInstructors instructors = new();

    private AdminInstructorUseCases UseCases() => new(
        new InstructorUseCases(instructors, backOffice, TimeProvider.System),
        backOffice,
        new AuthorizationUseCases(backOffice, backOffice, backOffice),
        new CreateAdministratorAccountHandler(backOffice, backOffice, backOffice, backOffice,
            new CreateAdministratorAccountCommandValidator(), TimeProvider.System));

    [Fact]
    public void Controller_RequiresInstructorsManagePermission()
    {
        var attribute = Assert.Single(typeof(AdminInstructorsController)
            .GetCustomAttributes(typeof(AuthorizeAttribute), true).Cast<AuthorizeAttribute>());
        Assert.Equal(Permissions.InstructorsManage, attribute.Policy);
    }

    [Fact]
    public async Task Create_AddsPersonRoleAndLinkedInstructor()
    {
        var result = await UseCases().CreateAsync(
            new CreateInstructorInput("Lucía", "Paredes", null, "lucia@matefacil.pe", null, null), default);

        Assert.Equal(AdminResultStatus.Ok, result.Status);
        var userId = result.Value!.Credentials.UserId;
        Assert.Equal("lucia@matefacil.pe", result.Value.Credentials.LoginIdentifier);
        Assert.Equal([Roles.Instructor], (await backOffice.GetRoleCodesByUserIdsAsync([userId], default))[userId]);
        Assert.Equal(userId, Assert.Single(instructors.Items).UserId);
    }

    [Fact]
    public async Task Create_WithDuplicateEmail_IsAConflict_AndCreatesNoInstructor()
    {
        var useCases = UseCases();
        await useCases.CreateAsync(new CreateInstructorInput("Lucía", "Paredes", null, "lucia@matefacil.pe", null, null), default);

        var duplicate = await useCases.CreateAsync(
            new CreateInstructorInput("Otra", "Persona", null, "LUCIA@matefacil.pe", null, null), default);

        Assert.Equal(AdminResultStatus.Conflict, duplicate.Status);
        Assert.Single(instructors.Items);
    }

    [Fact]
    public async Task List_ResolvesNamesInOneBatch_SortedByName()
    {
        var useCases = UseCases();
        await useCases.CreateAsync(new CreateInstructorInput("Zoe", "Ramos", null, "zoe@matefacil.pe", null, null), default);
        await useCases.CreateAsync(new CreateInstructorInput("Ana", "Lima", null, "ana@matefacil.pe", null, null), default);
        instructors.Items.Add(Instructor.Create(Guid.NewGuid(), DateTimeOffset.UtcNow));

        var list = await useCases.ListAsync(default);

        Assert.Equal(3, list.Count);
        Assert.Equal("(profesor sin persona vinculada)", list[0].FullName);
        Assert.Equal(["Ana Lima", "Zoe Ramos"], list.Skip(1).Select(item => item.FullName));
    }

    private sealed class FakeInstructors : IInstructorRepository
    {
        public List<Instructor> Items { get; } = [];

        public Task<Instructor?> GetAsync(Guid instructorId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.SingleOrDefault(item => item.Id == instructorId));

        public Task<IReadOnlyList<Instructor>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Instructor>>(Items.ToList());

        public Task<bool> AllExistAsync(IReadOnlyCollection<Guid> instructorIds, CancellationToken cancellationToken) =>
            Task.FromResult(instructorIds.All(id => Items.Any(item => item.Id == id)));

        public Task<bool> UserLinkedElsewhereAsync(Guid userId, Guid instructorId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Any(item => item.UserId == userId && item.Id != instructorId));

        public void Add(Instructor instructor) => Items.Add(instructor);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
