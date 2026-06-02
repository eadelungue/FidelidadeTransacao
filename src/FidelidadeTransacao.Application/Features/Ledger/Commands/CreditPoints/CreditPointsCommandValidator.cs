using FluentValidation;

namespace FidelidadeTransacao.Application.Features.Ledger.Commands.CreditPoints;

public sealed class CreditPointsCommandValidator : AbstractValidator<CreditPointsCommand>
{
    public CreditPointsCommandValidator()
    {
        RuleFor(x => x.AccountId)
            .NotEmpty().WithMessage("AccountId é obrigatório.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount deve ser maior que zero.")
            .LessThanOrEqualTo(10_000_000).WithMessage("Amount excede o limite máximo por operação.");

        RuleFor(x => x.OccurrenceDate)
            .NotEmpty().WithMessage("OccurrenceDate é obrigatória.")
            .LessThanOrEqualTo(DateTimeOffset.UtcNow.AddMinutes(5))
            .WithMessage("OccurrenceDate não pode ser uma data futura.");

        RuleFor(x => x.IdempotencyKey)
            .NotEmpty().WithMessage("IdempotencyKey é obrigatória.")
            .MaximumLength(256).WithMessage("IdempotencyKey não pode exceder 256 caracteres.")
            // Apenas caracteres alfanuméricos, hífens e underscores — previne injection
            .Matches(@"^[a-zA-Z0-9\-_:]+$").WithMessage("IdempotencyKey contém caracteres inválidos.");

        RuleFor(x => x.CorrelationId)
            .NotEmpty().WithMessage("CorrelationId é obrigatório.")
            .MaximumLength(128);

        RuleFor(x => x.PartnerId)
            .NotEmpty().WithMessage("PartnerId é obrigatório.")
            .MaximumLength(50);

        RuleFor(x => x.ProductName)
            .NotEmpty().WithMessage("ProductName é obrigatório.")
            .MaximumLength(200)
            // Proteção XSS: rejeita tags HTML
            .Matches(@"^[^<>""'%;()&+]*$").WithMessage("ProductName contém caracteres inválidos.");

        RuleFor(x => x.Description)
            .MaximumLength(500).When(x => x.Description is not null)
            .Matches(@"^[^<>""'%;()&+]*$").When(x => x.Description is not null)
            .WithMessage("Description contém caracteres inválidos.");
    }
}
