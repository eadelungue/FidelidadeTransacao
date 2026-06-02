using FidelidadeTransacao.Domain.ValueObjects;
using MediatR;

namespace FidelidadeTransacao.Domain.Events;

public sealed record PointsDebitedEvent(
    Guid EventId,
    Guid TransactionId,
    Guid AccountId,
    decimal Amount,
    string CorrelationId,
    StatementMetadata StatementMetadata) : INotification;
