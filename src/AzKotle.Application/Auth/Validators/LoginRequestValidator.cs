using FluentValidation;

namespace AzKotle.Application.Auth.Validators;

public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email)
            .NotEmpty().WithMessage("Email nesmí být prázdný.")
            .EmailAddress().WithMessage("Email nemá platný formát.");

        RuleFor(x => x.Password)
            .NotEmpty().WithMessage("Heslo nesmí být prázdné.");
    }
}
