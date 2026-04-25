using AzKotle.Domain.Entities.Inspections;
using FluentValidation;

namespace AzKotle.Application.Inspections;

public sealed class CreateInspectionRequestValidator : AbstractValidator<CreateInspectionRequest>
{
    public CreateInspectionRequestValidator()
    {
        RuleFor(x => x.BoilerId).NotEmpty().WithMessage("Kotel je povinný.");
        RuleFor(x => x.Type).IsInEnum();
        RuleFor(x => x.PerformedAt)
            .Must(d => d <= DateTime.UtcNow.AddMinutes(5))
            .WithMessage("Datum revize nesmí být v budoucnosti.");
    }
}

public sealed class UpdateInspectionDraftRequestValidator : AbstractValidator<UpdateInspectionDraftRequest>
{
    public UpdateInspectionDraftRequestValidator()
    {
        RuleFor(x => x.FormDataJson).NotNull().WithMessage("FormDataJson musí být JSON objekt.");
        RuleFor(x => x.Findings).MaximumLength(Inspection.FindingsMaxLength);
        RuleFor(x => x.Recommendations).MaximumLength(Inspection.RecommendationsMaxLength);
    }
}
