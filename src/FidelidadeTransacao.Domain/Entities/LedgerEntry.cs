using FidelidadeTransacao.Domain.Common;
using FidelidadeTransacao.Domain.Enums;
using FidelidadeTransacao.Domain.Events;
using FidelidadeTransacao.Domain.ValueObjects;

namespace FidelidadeTransacao.Domain.Entities;

/// <summary>
/// Entidade imutável que representa um lançamento no Ledger.
///
/// RN05 — Histórico Imutável:
/// - Todos os setters são privados e definidos apenas no factory method.
/// - Não existe método de Update nesta entidade.
/// - Na camada de Infrastructure, o repositório só executa INSERT — nunca UPDATE/DELETE.
/// - O EF Core (se usado) seria configurado com .HasNoKey() ou sem migrations de alter.
///   Com Dapper, o SQL de INSERT é explícito e não há risco de UPDATE acidental.
///
/// RN05 — Estorno como novo lançamento:
/// - Um Refund é um novo LedgerEntry do tipo Refund com referência ao original.
/// - O lançamento original permanece intacto no banco.
/// </summary>
public sealed class LedgerEntry : AuditableEntity
{
    private LedgerEntry() { }

    public Guid AccountId { get; private set; }
    public EntryType Type { get; private set; }
    public decimal Amount { get; private set; }
    public DateTimeOffset OccurrenceDate { get; private set; }
    public string IdempotencyKey { get; private set; } = string.Empty;
    public string CorrelationId { get; private set; } = string.Empty;

    // Referência ao lançamento original (preenchido apenas em Refunds)
    public Guid? OriginalEntryId { get; private set; }

    // Referência externa (ex: ID do pedido no e-commerce)
    public string? ReferenceId { get; private set; }

    // Motivo do estorno (preenchido apenas em Refunds)
    public string? RefundReason { get; private set; }

    // Value Object de metadados de extrato (armazenado como colunas separadas no banco)
    public StatementMetadata StatementMetadata { get; private set; } = null!;

    // ── Factory Methods ──────────────────────────────────────────────────────

    public static LedgerEntry CriarCredito(
        Guid accountId,
        decimal amount,
        DateTimeOffset occurrenceDate,
        string idempotencyKey,
        string correlationId,
        StatementMetadata statementMetadata)
    {
        var entry = CriarBase(accountId, EntryType.Credit, amount, occurrenceDate,
            idempotencyKey, correlationId, statementMetadata);

        entry.AddDomainEvent(new PointsCreditedEvent(
            EventId:         Guid.NewGuid(),
            TransactionId:   entry.Id,
            AccountId:       accountId,
            Amount:          amount,
            CorrelationId:   correlationId,
            StatementMetadata: statementMetadata));

        return entry;
    }

    public static LedgerEntry CriarDebito(
        Guid accountId,
        decimal amount,
        DateTimeOffset occurrenceDate,
        string idempotencyKey,
        string correlationId,
        string referenceId,
        StatementMetadata statementMetadata)
    {
        var entry = CriarBase(accountId, EntryType.Debit, amount, occurrenceDate,
            idempotencyKey, correlationId, statementMetadata);

        entry.ReferenceId = referenceId;

        entry.AddDomainEvent(new PointsDebitedEvent(
            EventId:         Guid.NewGuid(),
            TransactionId:   entry.Id,
            AccountId:       accountId,
            Amount:          amount,
            CorrelationId:   correlationId,
            StatementMetadata: statementMetadata));

        return entry;
    }

    public static LedgerEntry CriarEstorno(
        Guid accountId,
        Guid originalEntryId,
        decimal amount,
        DateTimeOffset occurrenceDate,
        string idempotencyKey,
        string correlationId,
        string reason,
        StatementMetadata statementMetadata)
    {
        var entry = CriarBase(accountId, EntryType.Refund, amount, occurrenceDate,
            idempotencyKey, correlationId, statementMetadata);

        entry.OriginalEntryId = originalEntryId;
        entry.RefundReason    = reason;

        entry.AddDomainEvent(new PointsRefundedEvent(
            EventId:           Guid.NewGuid(),
            TransactionId:     entry.Id,
            OriginalEntryId:   originalEntryId,
            AccountId:         accountId,
            Amount:            amount,
            CorrelationId:     correlationId,
            StatementMetadata: statementMetadata));

        return entry;
    }

    private static LedgerEntry CriarBase(
        Guid accountId,
        EntryType type,
        decimal amount,
        DateTimeOffset occurrenceDate,
        string idempotencyKey,
        string correlationId,
        StatementMetadata statementMetadata)
    {
        if (accountId == Guid.Empty)
            throw new ArgumentException("AccountId não pode ser vazio.");
        if (amount <= 0)
            throw new ArgumentException("Amount deve ser maior que zero.");
        if (string.IsNullOrWhiteSpace(idempotencyKey))
            throw new ArgumentException("IdempotencyKey é obrigatória.");
        if (string.IsNullOrWhiteSpace(correlationId))
            throw new ArgumentException("CorrelationId é obrigatório.");

        return new LedgerEntry
        {
            AccountId        = accountId,
            Type             = type,
            Amount           = amount,
            OccurrenceDate   = occurrenceDate,
            IdempotencyKey   = idempotencyKey,
            CorrelationId    = correlationId,
            StatementMetadata = statementMetadata,
            CriadoEm        = DateTimeOffset.UtcNow,
            CriadoPor       = "ledger-api"
        };
    }
}
