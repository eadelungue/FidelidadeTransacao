using FidelidadeTransacao.Domain.Entities;

namespace FidelidadeTransacao.Domain.Interfaces;

/// <summary>
/// Contrato do repositório de lançamentos do Ledger.
/// IMPORTANTE: Apenas INSERT é permitido — sem UPDATE ou DELETE (RN05).
/// </summary>
public interface ILedgerEntryRepository
{
    /// <summary>
    /// Insere um novo lançamento. Operação append-only — nunca atualiza existentes.
    /// </summary>
    Task InserirAsync(LedgerEntry entry, CancellationToken ct = default);

    Task<LedgerEntry?> ObterPorIdAsync(Guid entryId, CancellationToken ct = default);

    Task<IEnumerable<LedgerEntry>> ObterPorContaAsync(
        Guid accountId,
        int pagina,
        int tamanhoPagina,
        CancellationToken ct = default);
}
