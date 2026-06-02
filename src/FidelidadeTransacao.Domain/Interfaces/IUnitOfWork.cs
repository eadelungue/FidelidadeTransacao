namespace FidelidadeTransacao.Domain.Interfaces;

/// <summary>
/// Abstração do Unit of Work para controle transacional.
/// Com Dapper, gerenciamos a transação SQL explicitamente via IDbTransaction.
/// O UoW expõe BeginTransaction/Commit/Rollback para que os handlers
/// possam envolver múltiplas operações de repositório em uma única transação ACID.
///
/// FLUXO CRÍTICO (RN01 + RN02):
/// BeginTransaction()
///   → ObterComLock(accountId)   ← UPDLOCK aplicado aqui
///   → ValidarSaldo()
///   → InserirLançamento()
///   → AtualizarSaldo()
///   → InserirIdempotência()
/// Commit()
/// → PublicarEventos()           ← APÓS commit (consistência eventual)
/// </summary>
public interface IUnitOfWork : IAsyncDisposable
{
    Task BeginTransactionAsync(CancellationToken ct = default);
    Task CommitAsync(CancellationToken ct = default);
    Task RollbackAsync(CancellationToken ct = default);
}
