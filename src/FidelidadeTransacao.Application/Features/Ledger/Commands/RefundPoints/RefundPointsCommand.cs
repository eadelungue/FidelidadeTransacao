using MediatR;

namespace FidelidadeTransacao.Application.Features.Ledger.Commands.RefundPoints;

/// <summary>
/// Command CQRS para estorno de pontos (Refund).
/// OriginalTransactionId referencia o lançamento que está sendo desfeito.
/// O lançamento original NUNCA é alterado — um novo lançamento compensatório é criado (RN05).
/// </summary>
public sealed record RefundPointsCommand(
    Guid AccountId,
    Guid OriginalTransactionId,
    decimal Amount,
    DateTimeOffset OccurrenceDate,
    string Reason,
    string IdempotencyKey,
    string CorrelationId,
    string PartnerId,
    string ProductName,
    string? Description) : IRequest<LedgerOperationResult>;
