using FidelidadeTransacao.Domain.Entities;

namespace FidelidadeTransacao.Domain.Interfaces;

/// <summary>
/// Contrato para persistência dos registros de idempotência (RN03).
/// </summary>
public interface IIdempotencyRepository
{
    /// <summary>
    /// Busca um registro de idempotência não expirado pela chave.
    /// Retorna null se não existir ou se estiver expirado.
    /// </summary>
    Task<IdempotencyRecord?> ObterPorChaveAsync(string idempotencyKey, CancellationToken ct = default);

    /// <summary>
    /// Persiste o registro de idempotência atomicamente com o lançamento.
    /// Deve ser chamado dentro da mesma transação SQL.
    /// </summary>
    Task InserirAsync(IdempotencyRecord record, CancellationToken ct = default);
}
