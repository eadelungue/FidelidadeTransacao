using FidelidadeTransacao.Domain.Entities;

namespace FidelidadeTransacao.Domain.Interfaces;

/// <summary>
/// Contrato do repositório de contas do Ledger.
/// Definido no Domain (DIP) — implementação concreta com Dapper fica na Infrastructure.
/// </summary>
public interface ILedgerAccountRepository
{
    /// <summary>
    /// Busca a conta e aplica PESSIMISTIC LOCK (UPDLOCK + ROWLOCK) na linha.
    /// Deve ser chamado DENTRO de uma transação SQL ativa.
    /// Implementa a RN02 — garante que apenas uma operação por conta ocorre por vez.
    /// </summary>
    Task<LedgerAccount?> ObterComLockAsync(Guid accountId, CancellationToken ct = default);

    Task<LedgerAccount?> ObterPorIdAsync(Guid accountId, CancellationToken ct = default);

    Task AdicionarAsync(LedgerAccount account, CancellationToken ct = default);

    /// <summary>
    /// Atualiza APENAS o saldo da conta.
    /// Único UPDATE permitido nesta entidade — e apenas dentro de transação com lock.
    /// </summary>
    Task AtualizarSaldoAsync(Guid accountId, decimal novoSaldo, CancellationToken ct = default);
}
