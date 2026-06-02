using FluentValidation;

namespace FidelidadeTransacao.Application.Features.Ledger.Commands.DebitPoints;

public sealed class DebitPointsCommandValidator : AbstractValidator<DebitPointsCommand>
{
    public DebitPointsCommandValidator()
    {
        RuleFor(x => x.AccountId)
            .NotEmpty().WithMessage("AccountId é obrigatório.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount deve ser maior que zero.")
            .LessThanOrEqualTo(10_000_000).WithMessage("Amount excede o limite máximo por operação.");

        RuleFor(x => x.OccurrenceDate)
            .NotEmpty().WithMessage("OccurrenceDate é obrigatória.");

        RuleFor(x => x.ReferenceId)
            .NotEmpty().WithMessage("ReferenceId é obrigatório.")
            .MaximumLength(256)
            .Matches(@"^[a-zA-Z0-9\-_:]+$").WithMessage("ReferenceId contém caracteres inválidos.");

        RuleFor(x => x.IdempotencyKey)
            .NotEmpty().WithMessage("IdempotencyKey é obrigatória.")
            .MaximumLength(256)
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
            .Matches(@"^[^<>""'%;()&+]*$").WithMessage("ProductName contém caracteres inválidos.");
    }
}
