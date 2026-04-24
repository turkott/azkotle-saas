using AzKotle.Domain.Entities.Tenants;
using AzKotle.Domain.Entities.Users;
using FluentValidation;

namespace AzKotle.Application.Auth.Validators;

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public const int MinPasswordLength = 12;

    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email nesmí být prázdný.")
            .MaximumLength(User.EmailMaxLength).WithMessage($"Email může mít max {User.EmailMaxLength} znaků.")
            .EmailAddress().WithMessage("Email nemá platný formát.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Heslo nesmí být prázdné.")
            .MinimumLength(MinPasswordLength).WithMessage($"Heslo musí mít aspoň {MinPasswordLength} znaků.");

        RuleFor(x => x.FullName)
            .NotEmpty().WithMessage("Jméno nesmí být prázdné.")
            .MaximumLength(User.FullNameMaxLength).WithMessage($"Jméno může mít max {User.FullNameMaxLength} znaků.");

        RuleFor(x => x.TenantSlug)
            .NotEmpty().WithMessage("Slug nesmí být prázdný.")
            .MaximumLength(Tenant.SlugMaxLength).WithMessage($"Slug může mít max {Tenant.SlugMaxLength} znaků.")
            .Matches("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$")
            .WithMessage("Slug musí obsahovat jen malá písmena, čísla a pomlčky.");

        RuleFor(x => x.CompanyName)
            .NotEmpty().WithMessage("Název firmy nesmí být prázdný.")
            .MaximumLength(Tenant.CompanyNameMaxLength)
            .WithMessage($"Název firmy může mít max {Tenant.CompanyNameMaxLength} znaků.");

        RuleFor(x => x.Ico)
            .Matches("^[0-9]{8}$")
            .When(x => !string.IsNullOrWhiteSpace(x.Ico))
            .WithMessage("IČO musí mít přesně 8 číslic.");
    }
}
