using AzKotle.Domain.Entities.Tenants;
using FluentValidation;

namespace AzKotle.Application.Auth.Validators;

/// <summary>
/// /auth/refresh body validator — refresh token sám není v body (HttpOnly cookie),
/// validujeme jen volitelný TenantSlug.
/// </summary>
public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator()
    {
        RuleFor(x => x.TenantSlug)
            .MaximumLength(Tenant.SlugMaxLength)
            .When(x => !string.IsNullOrWhiteSpace(x.TenantSlug))
            .WithMessage($"Slug může mít max {Tenant.SlugMaxLength} znaků.");
    }
}
