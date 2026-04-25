using AzKotle.Domain.Entities.Locations;
using FluentValidation;

namespace AzKotle.Application.Locations;

public sealed class CreateLocationRequestValidator : AbstractValidator<CreateLocationRequest>
{
    public CreateLocationRequestValidator()
    {
        RuleFor(x => x.CustomerId).NotEmpty().WithMessage("Zákazník je povinný.");

        RuleFor(x => x.Street)
            .NotEmpty().WithMessage("Ulice nesmí být prázdná.")
            .MaximumLength(Location.StreetMaxLength);
        RuleFor(x => x.City)
            .NotEmpty().WithMessage("Město nesmí být prázdné.")
            .MaximumLength(Location.CityMaxLength);
        RuleFor(x => x.Zip)
            .NotEmpty().WithMessage("PSČ nesmí být prázdné.")
            .MaximumLength(Location.ZipMaxLength);

        RuleFor(x => x.GpsLatitude)
            .InclusiveBetween(-90m, 90m).When(x => x.GpsLatitude.HasValue)
            .WithMessage("Zeměpisná šířka musí být v rozsahu -90 až 90.");
        RuleFor(x => x.GpsLongitude)
            .InclusiveBetween(-180m, 180m).When(x => x.GpsLongitude.HasValue)
            .WithMessage("Zeměpisná délka musí být v rozsahu -180 až 180.");
    }
}

public sealed class UpdateLocationRequestValidator : AbstractValidator<UpdateLocationRequest>
{
    public UpdateLocationRequestValidator()
    {
        RuleFor(x => x.Street)
            .NotEmpty().WithMessage("Ulice nesmí být prázdná.")
            .MaximumLength(Location.StreetMaxLength);
        RuleFor(x => x.City)
            .NotEmpty().WithMessage("Město nesmí být prázdné.")
            .MaximumLength(Location.CityMaxLength);
        RuleFor(x => x.Zip)
            .NotEmpty().WithMessage("PSČ nesmí být prázdné.")
            .MaximumLength(Location.ZipMaxLength);

        RuleFor(x => x.GpsLatitude)
            .InclusiveBetween(-90m, 90m).When(x => x.GpsLatitude.HasValue);
        RuleFor(x => x.GpsLongitude)
            .InclusiveBetween(-180m, 180m).When(x => x.GpsLongitude.HasValue);
    }
}
