using Dapper;
using FidelidadeTransacao.Domain.Entities;
using FidelidadeTransacao.Domain.Enums;
using FidelidadeTransacao.Domain.Interfaces;
using FidelidadeTransacao.Domain.ValueObjects;

namespace FidelidadeTransacao.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositório de lançamentos do Ledger usando Dapper.
///
/// CQRS:
/// - InserirAsync usa UnitOfWork (Write — banco primário, dentro de transação).
/// - ObterPorIdAsync e ObterPorContaAsync usam IReadDbConnectionFactory (réplica).
///
/// RN05 — Histórico Imutável:
/// Este repositório contém APENAS operações de INSERT e SELECT.
/// Não existe método Update() ou Delete() — por design.
/// </summary>
public sealed class LedgerEntryRepository(
    UnitOfWork unitOfWork,
    IReadDbConnectionFactory readFactory) : ILedgerEntryRepository
{
    public async Task InserirAsync(LedgerEntry entry, CancellationToken ct = default)
    {
        // INSERT sempre no banco primário via UnitOfWork.
        const string sql = """
            INSERT INTO LedgerEntries (
                Id, AccountId, Type, Amount, OccurrenceDate,
                IdempotencyKey, CorrelationId, OriginalEntryId, ReferenceId, RefundReason,
                PartnerId, ProductName, Description,
                CriadoEm, CriadoPor
            ) VALUES (
                @Id, @AccountId, @Type, @Amount, @OccurrenceDate,
                @IdempotencyKey, @CorrelationId, @OriginalEntryId, @ReferenceId, @RefundReason,
                @PartnerId, @ProductName, @Description,
                @CriadoEm, @CriadoPor
            )
            """;

        await unitOfWork.Connection.ExecuteAsync(sql, new
        {
            entry.Id,
            entry.AccountId,
            Type             = entry.Type.ToString(),
            entry.Amount,
            entry.OccurrenceDate,
            entry.IdempotencyKey,
            entry.CorrelationId,
            entry.OriginalEntryId,
            entry.ReferenceId,
            entry.RefundReason,
            PartnerId        = entry.StatementMetadata.PartnerId,
            ProductName      = entry.StatementMetadata.ProductName,
            Description      = entry.StatementMetadata.Description,
            entry.CriadoEm,
            entry.CriadoPor
        }, transaction: unitOfWork.CurrentTransaction);
    }

    public async Task<LedgerEntry?> ObterPorIdAsync(Guid entryId, CancellationToken ct = default)
    {
        // Leitura simples — usa réplica (Read connection), sem transação.
        const string sql = """
            SELECT
                Id, AccountId, Type, Amount, OccurrenceDate,
                IdempotencyKey, CorrelationId, OriginalEntryId, ReferenceId, RefundReason,
                PartnerId, ProductName, Description,
                CriadoEm, CriadoPor
            FROM LedgerEntries
            WHERE Id = @EntryId
            """;

        using var connection = readFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<LedgerEntryRow>(
            sql,
            new { EntryId = entryId });

        return row is null ? null : MapToDomain(row);
    }

    public async Task<IEnumerable<LedgerEntry>> ObterPorContaAsync(
        Guid accountId,
        int pagina,
        int tamanhoPagina,
        CancellationToken ct = default)
    {
        // Leitura paginada — usa réplica (Read connection), sem transação.
        const string sql = """
            SELECT
                Id, AccountId, Type, Amount, OccurrenceDate,
                IdempotencyKey, CorrelationId, OriginalEntryId, ReferenceId, RefundReason,
                PartnerId, ProductName, Description,
                CriadoEm, CriadoPor
            FROM LedgerEntries
            WHERE AccountId = @AccountId
            ORDER BY CriadoEm DESC
            OFFSET @Offset ROWS FETCH NEXT @TamanhoPagina ROWS ONLY
            """;

        using var connection = readFactory.CreateConnection();
        var rows = await connection.QueryAsync<LedgerEntryRow>(
            sql,
            new
            {
                AccountId     = accountId,
                Offset        = (pagina - 1) * tamanhoPagina,
                TamanhoPagina = tamanhoPagina
            });

        return rows.Select(MapToDomain);
    }

    private static LedgerEntry MapToDomain(LedgerEntryRow row)
    {
        var entry = (LedgerEntry)Activator.CreateInstance(typeof(LedgerEntry), nonPublic: true)!;

        SetProperty(entry, nameof(LedgerEntry.Id), row.Id);
        SetProperty(entry, nameof(LedgerEntry.AccountId), row.AccountId);
        SetProperty(entry, nameof(LedgerEntry.Type), Enum.Parse<EntryType>(row.Type));
        SetProperty(entry, nameof(LedgerEntry.Amount), row.Amount);
        SetProperty(entry, nameof(LedgerEntry.OccurrenceDate), row.OccurrenceDate);
        SetProperty(entry, nameof(LedgerEntry.IdempotencyKey), row.IdempotencyKey);
        SetProperty(entry, nameof(LedgerEntry.CorrelationId), row.CorrelationId);
        SetProperty(entry, nameof(LedgerEntry.OriginalEntryId), row.OriginalEntryId);
        SetProperty(entry, nameof(LedgerEntry.ReferenceId), row.ReferenceId);
        SetProperty(entry, nameof(LedgerEntry.RefundReason), row.RefundReason);
        SetProperty(entry, nameof(LedgerEntry.CriadoEm), row.CriadoEm);
        SetProperty(entry, nameof(LedgerEntry.CriadoPor), row.CriadoPor);
        SetProperty(entry, nameof(LedgerEntry.StatementMetadata),
            StatementMetadata.Create(row.PartnerId, row.ProductName, row.Description));

        return entry;
    }

    private static void SetProperty(object obj, string propertyName, object? value)
    {
        var prop = obj.GetType().GetProperty(propertyName)
            ?? throw new InvalidOperationException($"Propriedade '{propertyName}' não encontrada.");
        prop.SetValue(obj, value);
    }

    private sealed record LedgerEntryRow(
        Guid Id, Guid AccountId, string Type, decimal Amount,
        DateTimeOffset OccurrenceDate, string IdempotencyKey, string CorrelationId,
        Guid? OriginalEntryId, string? ReferenceId, string? RefundReason,
        string PartnerId, string ProductName, string? Description,
        DateTimeOffset CriadoEm, string CriadoPor);
}
