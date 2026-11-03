using FluentValidation;

namespace AulaVirtual.Modules.Courses.Application.Courses;

public sealed class SaveCourseValidator : AbstractValidator<SaveCourseCommand>
{
    public SaveCourseValidator()
    {
        RuleFor(command => command.Title).NotEmpty().MaximumLength(200);
        RuleFor(command => command.CategoryId).Must(id => id != Guid.Empty)
            .WithMessage("Category identifier cannot be empty.");
        RuleFor(command => command.SessionDurationMinutes)
            .InclusiveBetween(Domain.Courses.Course.MinSessionMinutes, Domain.Courses.Course.MaxSessionMinutes)
            .When(command => command.SessionDurationMinutes is not null);
    }
}
