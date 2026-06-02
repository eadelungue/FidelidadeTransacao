using FidelidadeTransacao.Application.Common.Interfaces;
using FidelidadeTransacao.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FidelidadeTransacao.Application.Features.Ledger.EventHandlers;

public sealed class PointsRefundedEventHandler(
    IEventPublisher eventPublisher,
    ILogger<PointsRefundedEventHandler> logger) : INotificationHandler<PointsRefundedEvent>
{
    private const string TopicName = "ledger-points-refunded";

    public async Task Handle(PointsRefundedEvent notification, CancellationToken cancellationToken)
    {
        var envelope = new
        {
            EventId       = notification.EventId,
            EventType     = "PointsRefunded",
            Timestamp     = DateTimeOffset.UtcNow,
            CorrelationId = notification.CorrelationId,
            Payload       = new
            {
                TransactionId    = notification.TransactionId,
                OriginalEntryId  = notification.OriginalEntryId,
                AccountId        = notification.AccountId,
                Amount           = notification.Amount,
                StatementDetails = new
                {
                    notification.StatementMetadata.PartnerId,
                    notification.StatementMetadata.ProductName,
                    notification.StatementMetadata.Description
                }
            }
        };

        logger.LogInformation(
            "[Ledger][Event] Publicando PointsRefunded. EventId: {EventId} | CorrelationId: {CorrelationId}",
            notification.EventId, notification.CorrelationId);

        await eventPublisher.PublicarAsync(TopicName, envelope, cancellationToken);
    }
}
