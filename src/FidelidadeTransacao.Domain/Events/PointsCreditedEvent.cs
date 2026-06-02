using FidelidadeTransacao.Domain.ValueObjects;
using MediatR;

namespace FidelidadeTransacao.Domain.Events;

/// <summary>
/// Evento de domínio disparado após confirmação de crédito de pontos.
/// Será capturado pelo EventHandler na Application layer e publicado no Service Bus.
/// Estrutura alinhada com a especificação de saída (item 5 do documento).
/// </summary>
public sealed record PointsCreditedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid AccountId,
    decimal Amount,
    string CorrelationId,
    StatementMetadata StatementMetadata) : INotification;
