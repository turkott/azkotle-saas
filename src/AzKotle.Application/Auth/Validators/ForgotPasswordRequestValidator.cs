using AzKotle.Domain.Entities.Tenants;
using AzKotle.Domain.Entities.Users;
using FluentValidation;

namespace AzKotle.Application.Auth.Validators;

public sealed class ForgotPasswordRequestValidator : AbstractValidator<ForgotPasswordRequest>
{
    public ForgotPasswordRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email nesmí být prázdný.")
            .MaximumLength(User.EmailMaxLength).WithMessage($"Email může mít max {User.EmailMaxLength} znaků.")
            .EmailAddress().WithMessage("Email nemá platný formát.");

        RuleFor(x => x.TenantSlug)
            .NotEmpty().WithMessage("Slug firmy nesmí být prázdný.")
            .MaximumLength(Tenant.SlugMaxLength).WithMessage($"Slug může mít max {Tenant.SlugMaxLength} znaků.")
            .Matches("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$").WithMessage("Slug má neplatný formát.");
    }
}
