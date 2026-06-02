using FidelidadeTransacao.Application.Common.Interfaces;
using FidelidadeTransacao.Domain.Events;
using MediatR;
using Microsoft.Extensions.Logging;

namespace FidelidadeTransacao.Application.Features.Ledger.EventHandlers;

/// <summary>
/// Captura o PointsCreditedEvent e publica no Azure Service Bus.
/// Executa APÓS o commit do banco — consistência eventual intencional.
///
/// PAYLOAD publicado segue a especificação do item 5 do documento:
/// eventId, eventType, timestamp, correlationId, payload { transactionId, accountId, amount, statementDetails }
/// </summary>
public sealed class PointsCreditedEventHandler(
    IEventPublisher eventPublisher,
    ILogger<PointsCreditedEventHandler> logger) : INotificationHandler<PointsCreditedEvent>
{
    private const string TopicName = "ledger-points-credited";

    public async Task Handle(PointsCreditedEvent notification, CancellationToken cancellationToken)
    {
        var envelope = new
        {
            EventId       = notification.EventId,
            EventType     = "PointsCredited",
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
            "[Ledger][Event] Publicando PointsCredited. EventId: {EventId} | CorrelationId: {CorrelationId}",
            notification.EventId, notification.CorrelationId);

        await eventPublisher.PublicarAsync(TopicName, envelope, cancellationToken);
    }
}
