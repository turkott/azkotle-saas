using AzKotle.Domain.Entities.Users;
using FluentValidation;

namespace AzKotle.Application.Tenants.Team.Validators;

public sealed class CreateTechnicianRequestValidator : AbstractValidator<CreateTechnicianRequest>
{
    public const int MinPasswordLength = 12;

    public CreateTechnicianRequestValidator()
    {
        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Jméno nesmí být prázdné.")
            .MaximumLength(User.FullNameMaxLength)
            .WithMessage($"Jméno může mít max {User.FullNameMaxLength} znaků.");

        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email nesmí být prázdný.")
            .MaximumLength(User.EmailMaxLength)
            .WithMessage($"Email může mít max {User.EmailMaxLength} znaků.")
            .EmailAddress().WithMessage("Email nemá platný formát.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Heslo nesmí být prázdné.")
            .MinimumLength(MinPasswordLength)
            .WithMessage($"Heslo musí mít aspoň {MinPasswordLength} znaků.");
    }
}
