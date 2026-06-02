using MediatR;

namespace FidelidadeTransacao.Application.Features.Ledger.Commands.DebitPoints;

/// <summary>
/// Command CQRS para resgate de pontos (Debit).
/// ReferenceId é obrigatório — rastreia o pedido no sistema de recompensas.
/// </summary>
public sealed record DebitPointsCommand(
    Guid AccountId,
    decimal Amount,
    DateTimeOffset OccurrenceDate,
    string ReferenceId,
    string IdempotencyKey,
    string CorrelationId,
    string PartnerId,
    string ProductName,
    string? Description) : IRequest<LedgerOperationResult>;
