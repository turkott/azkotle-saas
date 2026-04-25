using AzKotle.Domain.Entities.Boilers;
using FluentValidation;

namespace AzKotle.Application.Boilers;

public sealed class CreateBoilerRequestValidator : AbstractValidator<CreateBoilerRequest>
{
    public CreateBoilerRequestValidator()
    {
        RuleFor(x => x.LocationId).NotEmpty().WithMessage("Lokalita je povinná.");
        RuleFor(x => x.Manufacturer)
            .NotEmpty().WithMessage("Výrobce nesmí být prázdný.")
            .MaximumLength(Boiler.ManufacturerMaxLength);
        RuleFor(x => x.Model)
            .NotEmpty().WithMessage("Model nesmí být prázdný.")
            .MaximumLength(Boiler.ModelMaxLength);
        RuleFor(x => x.SerialNo)
            .NotEmpty().WithMessage("Sériové číslo nesmí být prázdné.")
            .MaximumLength(Boiler.SerialNoMaxLength);
        RuleFor(x => x.OutputKw)
            .GreaterThan(0m).LessThanOrEqualTo(9999.9m)
            .WithMessage("Výkon musí být v rozsahu (0; 9999.9] kW.");
        RuleFor(x => x.FuelType).IsInEnum();
        RuleFor(x => x.InstalledAt)
            .Must(d => d <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Datum instalace nesmí být v budoucnosti.");
    }
}

public sealed class UpdateBoilerRequestValidator : AbstractValidator<UpdateBoilerRequest>
{
    public UpdateBoilerRequestValidator()
    {
        RuleFor(x => x.Manufacturer).NotEmpty().MaximumLength(Boiler.ManufacturerMaxLength);
        RuleFor(x => x.Model).NotEmpty().MaximumLength(Boiler.ModelMaxLength);
        RuleFor(x => x.SerialNo).NotEmpty().MaximumLength(Boiler.SerialNoMaxLength);
        RuleFor(x => x.OutputKw).GreaterThan(0m).LessThanOrEqualTo(9999.9m);
        RuleFor(x => x.FuelType).IsInEnum();
    }
}

public sealed class RecordInspectionRequestValidator : AbstractValidator<RecordInspectionRequest>
{
    public RecordInspectionRequestValidator()
    {
        RuleFor(x => x.PerformedAt)
            .Must(d => d <= DateOnly.FromDateTime(DateTime.UtcNow))
            .WithMessage("Datum revize nesmí být v budoucnosti.");
        RuleFor(x => x.NextDueAt)
            .GreaterThan(x => x.PerformedAt).When(x => x.NextDueAt.HasValue)
            .WithMessage("Další revize musí být po aktuální.");
    }
}
