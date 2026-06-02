using MediatR;

namespace FidelidadeTransacao.Application.Features.Ledger.Commands.CreditPoints;

/// <summary>
/// Command CQRS para acúmulo de pontos (Credit).
/// Records são imutáveis — ideal para Commands (sem efeitos colaterais no objeto).
/// </summary>
public sealed record CreditPointsCommand(
    Guid AccountId,
    decimal Amount,
    DateTimeOffset OccurrenceDate,
    string IdempotencyKey,
    string CorrelationId,
    // Metadados de extrato
    string PartnerId,
    string ProductName,
    string? Description) : IRequest<LedgerOperationResult>;
