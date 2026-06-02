using MediatR;

namespace FidelidadeTransacao.Domain.Common;

/// <summary>
/// Classe base para todas as entidades de domínio.
/// Gerencia o ciclo de vida dos Domain Events (padrão DDD).
/// Os eventos são publicados APÓS o commit da transação de banco,
/// garantindo consistência entre persistência e mensageria (outbox pattern simplificado).
/// </summary>
public abstract class BaseEntity
{
    public Guid Id { get; protected set; } = Guid.NewGuid();

    private readonly List<INotification> _domainEvents = [];
    public IReadOnlyCollection<INotification> DomainEvents => _domainEvents.AsReadOnly();

    protected void AddDomainEvent(INotification domainEvent) => _domainEvents.Add(domainEvent);
    public void ClearDomainEvents() => _domainEvents.Clear();
}
