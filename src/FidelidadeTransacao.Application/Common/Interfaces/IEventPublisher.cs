namespace FidelidadeTransacao.Application.Common.Interfaces;

/// <summary>
/// Abstração para publicação de eventos no broker de mensageria.
/// A Application depende desta interface (DIP) — a implementação Azure Service Bus
/// fica na Infrastructure, permitindo troca de broker sem impacto na Application.
/// </summary>
public interface IEventPublisher
{
    Task PublicarAsync<TEvent>(string topicName, TEvent eventData, CancellationToken ct = default)
        where TEvent : class;
}
