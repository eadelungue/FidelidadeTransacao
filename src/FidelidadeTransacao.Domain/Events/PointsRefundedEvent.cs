using FidelidadeTransacao.Domain.ValueObjects;
using MediatR;

namespace FidelidadeTransacao.Domain.Events;

public sealed record PointsRefundedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid OriginalEntryId,
    Guid AccountId,
    decimal Amount,
    string CorrelationId,
    StatementMetadata StatementMetadata) : INotification;
