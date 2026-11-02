using AulaVirtual.Modules.Administration.Application.Users;

namespace AulaVirtual.Modules.Administration.Api.Contracts;

public sealed record CreateStudentRequest(
    string GivenNames,
    string FirstSurname,
    string? SecondSurname,
    string? Email,
    string DocumentType,
    string DocumentNumber,
    Guid? GuardianUserId)
{
    public CreateStudentInput ToInput() => new(
        GivenNames, FirstSurname, SecondSurname, Email, DocumentType, DocumentNumber, GuardianUserId);
}

public sealed record CreateGuardianRequest(
    string GivenNames,
    string FirstSurname,
    string? SecondSurname,
    string Email,
    string? DocumentType,
    string? DocumentNumber)
{
    public CreateGuardianInput ToInput() => new(
        GivenNames, FirstSurname, SecondSurname, Email, DocumentType, DocumentNumber);
}

public sealed record SetUserStatusRequest(bool IsActive);

public sealed record IssuedCredentialsResponse(Guid UserId, string LoginIdentifier, string InitialPassword)
{
    public static IssuedCredentialsResponse From(IssuedCredentials credentials) =>
        new(credentials.UserId, credentials.LoginIdentifier, credentials.InitialPassword);
}
