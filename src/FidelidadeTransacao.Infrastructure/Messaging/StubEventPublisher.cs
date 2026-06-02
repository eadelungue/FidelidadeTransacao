using FidelidadeTransacao.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FidelidadeTransacao.Infrastructure.Messaging;

/// <summary>
/// Implementação stub do IEventPublisher para uso em desenvolvimento local.
/// Apenas loga o evento em vez de publicar no Azure Service Bus.
/// Registrado no DI quando ConnectionStrings:ServiceBus == "local-stub".
/// </summary>
public sealed class StubEventPublisher(
    ILogger<StubEventPublisher> logger) : IEventPublisher
{
    public Task PublicarAsync<TEvent>(
        string topicName,
        TEvent eventData,
        CancellationToken ct = default)
        where TEvent : class
    {
        var json = JsonSerializer.Serialize(eventData, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        });

        logger.LogInformation(
            "[STUB-ServiceBus] Evento que seria publicado no tópico '{Topic}':\n{Payload}",
            topicName,
            json);

        return Task.CompletedTask;
    }
}
