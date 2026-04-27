using AzKotle.Domain.Entities.Tenants;
using FluentValidation;

namespace AzKotle.Application.Auth.Validators;

public sealed class ResetPasswordRequestValidator : AbstractValidator<ResetPasswordRequest>
{
    public ResetPasswordRequestValidator()
    {
        RuleFor(x => x.Token)
            .NotEmpty().WithMessage("Token nesmí být prázdný.")
            .Length(20, 200).WithMessage("Token má neplatnou délku.");

        RuleFor(x => x.TenantSlug)
            .NotEmpty().WithMessage("Slug firmy nesmí být prázdný.")
            .MaximumLength(Tenant.SlugMaxLength).WithMessage($"Slug může mít max {Tenant.SlugMaxLength} znaků.")
            .Matches("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$").WithMessage("Slug má neplatný formát.");

        RuleFor(x => x.NewPassword)
            .NotEmpty().WithMessage("Heslo nesmí být prázdné.")
            .MinimumLength(RegisterRequestValidator.MinPasswordLength)
                .WithMessage($"Heslo musí mít aspoň {RegisterRequestValidator.MinPasswordLength} znaků.");
    }
}
