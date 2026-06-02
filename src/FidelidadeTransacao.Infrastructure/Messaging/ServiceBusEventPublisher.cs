using Azure.Messaging.ServiceBus;
using FidelidadeTransacao.Application.Common.Interfaces;
using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Text.Json;

namespace FidelidadeTransacao.Infrastructure.Messaging;

/// <summary>
/// Implementação do IEventPublisher usando Azure Service Bus.
///
/// DESIGN:
/// - ServiceBusClient é Singleton (thread-safe, gerencia pool de conexões AMQP)
/// - ServiceBusSender é cacheado por tópico para evitar overhead de criação
/// - CorrelationId do OpenTelemetry é propagado na mensagem para rastreamento distribuído
/// - Retry exponencial configurado no ServiceBusClient (3 tentativas, backoff 1s→30s)
/// </summary>
public sealed class ServiceBusEventPublisher(
    ServiceBusClient serviceBusClient,
    ILogger<ServiceBusEventPublisher> logger) : IEventPublisher, IAsyncDisposable
{
    private readonly Dictionary<string, ServiceBusSender> _senders = [];
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task PublicarAsync<TEvent>(
        string topicName,
        TEvent eventData,
        CancellationToken ct = default)
        where TEvent : class
    {
        var sender = await ObterSenderAsync(topicName);

        var json = JsonSerializer.Serialize(eventData, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        });

        var message = new ServiceBusMessage(json)
        {
            ContentType   = "application/json",
            Subject       = typeof(TEvent).Name,
            // Propaga o TraceId do OpenTelemetry para correlação no APM
            CorrelationId = Activity.Current?.TraceId.ToString() ?? Guid.NewGuid().ToString()
        };

        try
        {
            await sender.SendMessageAsync(message, ct);

            logger.LogInformation(
                "[ServiceBus] Mensagem publicada. Tópico: {Topic} | CorrelationId: {CorrelationId}",
                topicName, message.CorrelationId);
        }
        catch (ServiceBusException ex) when (ex.IsTransient)
        {
            logger.LogWarning(ex,
                "[ServiceBus] Erro transiente ao publicar. Tópico: {Topic}", topicName);
            throw; // Retry policy do ServiceBusClient cuida das tentativas
        }
    }

    private async Task<ServiceBusSender> ObterSenderAsync(string topicName)
    {
        if (_senders.TryGetValue(topicName, out var sender)) return sender;

        await _lock.WaitAsync();
        try
        {
            if (!_senders.TryGetValue(topicName, out sender))
            {
                sender = serviceBusClient.CreateSender(topicName);
                _senders[topicName] = sender;
            }
            return sender;
        }
        finally { _lock.Release(); }
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sender in _senders.Values)
            await sender.DisposeAsync();
        _lock.Dispose();
    }
}
