using AulaVirtual.Modules.Auth.Application.Accounts.CreateAccount;
using AulaVirtual.Modules.Authorization.Application;
using AulaVirtual.Modules.Courses.Application.Instructors;
using AulaVirtual.Modules.Users.Application.Abstractions;

namespace AulaVirtual.Modules.Administration.Application.Users;

public sealed record AdminInstructor(Guid Id, Guid? UserId, string FullName, string? Email);

public sealed record CreateInstructorInput(
    string GivenNames,
    string FirstSurname,
    string? SecondSurname,
    string Email,
    string? DocumentType,
    string? DocumentNumber);

public sealed record CreatedInstructor(Guid InstructorId, IssuedCredentials Credentials);

// Teachers (SCRUM-41). Courses only knows an Instructor id linked to a user; the name lives in Users.
// Listing costs two queries (instructors, then their people in one batch), never one per teacher.
public sealed class AdminInstructorUseCases(
    InstructorUseCases instructors,
    IUserDirectory users,
    AuthorizationUseCases authorization,
    CreateAdministratorAccountHandler createEmailAccount)
{
    public async Task<IReadOnlyList<AdminInstructor>> ListAsync(CancellationToken cancellationToken)
    {
        var list = await instructors.ListInstructorsAsync(cancellationToken);
        var userIds = list.Select(item => item.UserId).OfType<Guid>().ToHashSet();
        var people = (await users.GetManyAsync(userIds, cancellationToken)).ToDictionary(person => person.Id);

        return list
            .Select(item => item.UserId is { } userId && people.TryGetValue(userId, out var person)
                ? new AdminInstructor(item.Id, userId, person.FullName, person.Email)
                : new AdminInstructor(item.Id, item.UserId, "(profesor sin persona vinculada)", null))
            .OrderBy(item => item.FullName, StringComparer.CurrentCultureIgnoreCase)
            .ToArray();
    }

    // Steps: email account → instructor role → Instructor record linked to the person. Without a shared
    // transaction, a failure can only leave a person with a role and no Instructor record (visible in Users).
    public async Task<AdminResult<CreatedInstructor>> CreateAsync(CreateInstructorInput input,
        CancellationToken cancellationToken)
    {
        var account = await createEmailAccount.ExecuteAsync(
            new CreateAdministratorAccountCommand(input.GivenNames, input.FirstSurname, input.SecondSurname,
                input.Email, input.DocumentType, input.DocumentNumber),
            cancellationToken);

        switch (account.Status)
        {
            case CreateAccountStatus.Created:
                break;
            case CreateAccountStatus.Invalid:
                return AdminResult<CreatedInstructor>.Invalid("Revisa los datos ingresados.", account.Errors);
            case CreateAccountStatus.DuplicateLogin:
                return AdminResult<CreatedInstructor>.Conflict("Ya existe una cuenta con ese correo.");
            case CreateAccountStatus.DuplicateDocument:
                return AdminResult<CreatedInstructor>.Conflict("Ya hay una persona registrada con ese número de documento.");
            default:
                throw new InvalidOperationException($"Account creation failed: {account.Status}.");
        }

        var userId = account.UserId!.Value;
        await authorization.AssignRoleAsync(userId, Roles.Instructor, cancellationToken);
        var instructor = await instructors.CreateInstructorAsync(cancellationToken);
        var link = await instructors.LinkUserAsync(instructor.Id, userId, cancellationToken);
        if (link.Status != LinkUserStatus.Linked)
            throw new InvalidOperationException($"Instructor link failed: {link.Status}.");

        return AdminResult<CreatedInstructor>.Ok(new CreatedInstructor(instructor.Id,
            new IssuedCredentials(userId, account.LoginIdentifier!, account.InitialPassword!)));
    }
}
