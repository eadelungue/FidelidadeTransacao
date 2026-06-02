using FidelidadeTransacao.Application.Common.Interfaces;
using FidelidadeTransacao.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FidelidadeTransacao.Application.Features.Ledger.EventHandlers;

public sealed class PointsDebitedEventHandler(
    IEventPublisher eventPublisher,
    ILogger<PointsDebitedEventHandler> logger) : INotificationHandler<PointsDebitedEvent>
{
    private const string TopicName = "ledger-points-debited";

    public async Task Handle(PointsDebitedEvent notification, CancellationToken cancellationToken)
    {
        var envelope = new
        {
            EventId       = notification.EventId,
            EventType     = "PointsDebited",
            Timestamp     = DateTimeOffset.UtcNow,
            CorrelationId = notification.CorrelationId,
            Payload       = new
            {
                TransactionId    = notification.TransactionId,
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
            "[Ledger][Event] Publicando PointsDebited. EventId: {EventId} | CorrelationId: {CorrelationId}",
            notification.EventId, notification.CorrelationId);

        await eventPublisher.PublicarAsync(TopicName, envelope, cancellationToken);
    }
}
