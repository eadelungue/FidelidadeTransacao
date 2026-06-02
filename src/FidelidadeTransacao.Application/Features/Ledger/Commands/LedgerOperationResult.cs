namespace FidelidadeTransacao.Application.Features.Ledger.Commands;

/// <summary>
/// Resultado padronizado para todas as operações do Ledger.
/// Retornado pelo Handler e serializado como resposta HTTP 200/201.
/// Este mesmo payload é armazenado no IdempotencyRecord para reenvio (RN03).
/// </summary>
public sealed record LedgerOperationResult(
    Guid TransactionId,
    Guid AccountId,
    decimal Amount,
    decimal BalanceAfter,
    string EntryType,
    DateTimeOffset ProcessedAt,
    string CorrelationId,
    bool IsIdempotentReplay = false);
