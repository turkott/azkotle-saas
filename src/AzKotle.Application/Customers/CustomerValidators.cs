using AzKotle.Domain.Entities.Customers;
using FluentValidation;

namespace AzKotle.Application.Customers;

public sealed class CreateCustomerRequestValidator : AbstractValidator<CreateCustomerRequest>
{
    public CreateCustomerRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Jméno nesmí být prázdné.")
            .MaximumLength(Customer.NameMaxLength)
            .WithMessage($"Jméno může mít max {Customer.NameMaxLength} znaků.");

        RuleFor(x => x.Type).IsInEnum().WithMessage("Neplatný typ zákazníka.");

        RuleFor(x => x.Ico)
            .Matches("^[0-9]{8}$").When(x => !string.IsNullOrWhiteSpace(x.Ico))
            .WithMessage("IČO musí mít přesně 8 číslic.");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
            .WithMessage("Email nemá platný formát.")
            .MaximumLength(Customer.EmailMaxLength);

        RuleFor(x => x.Phone)
            .MaximumLength(Customer.PhoneMaxLength)
            .WithMessage($"Telefon může mít max {Customer.PhoneMaxLength} znaků.");
    }
}

public sealed class UpdateCustomerRequestValidator : AbstractValidator<UpdateCustomerRequest>
{
    public UpdateCustomerRequestValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Jméno nesmí být prázdné.")
            .MaximumLength(Customer.NameMaxLength);

        RuleFor(x => x.Ico)
            .Matches("^[0-9]{8}$").When(x => !string.IsNullOrWhiteSpace(x.Ico))
            .WithMessage("IČO musí mít přesně 8 číslic.");

        RuleFor(x => x.Email)
            .EmailAddress().When(x => !string.IsNullOrWhiteSpace(x.Email))
            .MaximumLength(Customer.EmailMaxLength);

        RuleFor(x => x.Phone)
            .MaximumLength(Customer.PhoneMaxLength);
    }
}
