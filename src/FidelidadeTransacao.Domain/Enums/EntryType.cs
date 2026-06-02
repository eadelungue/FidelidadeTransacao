namespace FidelidadeTransacao.Domain.Enums;

/// <summary>
/// Tipo do lançamento no Ledger.
/// Credit = entrada de pontos.
/// Debit  = saída de pontos.
/// Refund = lançamento compensatório (nunca altera o original — RN05).
/// </summary>
public enum EntryType
{
    Credit = 1,
    Debit  = 2,
    Refund = 3
}
