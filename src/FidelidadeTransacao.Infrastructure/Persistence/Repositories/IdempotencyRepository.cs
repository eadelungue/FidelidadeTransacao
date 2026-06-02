using Dapper;
using FidelidadeTransacao.Domain.Entities;
using FidelidadeTransacao.Domain.Interfaces;

namespace FidelidadeTransacao.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositório de registros de idempotência usando Dapper.
///
/// CQRS:
/// - ObterPorChaveAsync usa IReadDbConnectionFactory (réplica) — ocorre antes da transação.
/// - InserirAsync usa UnitOfWork (Write — banco primário, dentro da mesma transação do lançamento).
///
/// A leitura na réplica antes do lock é intencional: evita overhead no primário para a
/// verificação de idempotência, que é a operação mais frequente (cada request passa por ela).
/// Em caso de replicação com lag mínimo, a idempotência ainda é garantida pelo UNIQUE
/// constraint em IdempotencyKey no banco primário.
/// </summary>
public sealed class IdempotencyRepository(
    UnitOfWork unitOfWork,
    IReadDbConnectionFactory readFactory) : IIdempotencyRepository
{
    public async Task<IdempotencyRecord?> ObterPorChaveAsync(
        string idempotencyKey,
        CancellationToken ct = default)
    {
        // Leitura na réplica — ocorre ANTES da transação para evitar lock desnecessário.
        // Segurança adicional: UNIQUE constraint no banco primário garante atomicidade.
        const string sql = """
            SELECT IdempotencyKey, TransactionId, ResponsePayload, CriadoEm, ExpiresAt
            FROM IdempotencyRecords
            WHERE IdempotencyKey = @IdempotencyKey
              AND ExpiresAt > @Agora
            """;

        using var connection = readFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<IdempotencyRecordRow>(
            sql,
            new { IdempotencyKey = idempotencyKey, Agora = DateTimeOffset.UtcNow });

        if (row is null) return null;

        return IdempotencyRecord.Criar(row.IdempotencyKey, row.TransactionId, row.ResponsePayload);
    }

    public async Task InserirAsync(IdempotencyRecord record, CancellationToken ct = default)
    {
        // INSERT no banco primário via UnitOfWork — mesma transação do lançamento.
        const string sql = """
            INSERT INTO IdempotencyRecords (IdempotencyKey, TransactionId, ResponsePayload, CriadoEm, ExpiresAt)
            VALUES (@IdempotencyKey, @TransactionId, @ResponsePayload, @CriadoEm, @ExpiresAt)
            """;

        await unitOfWork.Connection.ExecuteAsync(sql, new
        {
            record.IdempotencyKey,
            record.TransactionId,
            record.ResponsePayload,
            record.CriadoEm,
            record.ExpiresAt
        }, transaction: unitOfWork.CurrentTransaction);
    }

    private sealed record IdempotencyRecordRow(
        string IdempotencyKey,
        Guid TransactionId,
        string ResponsePayload,
        DateTimeOffset CriadoEm,
        DateTimeOffset ExpiresAt);
}
