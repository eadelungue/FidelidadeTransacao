using Dapper;
using FidelidadeTransacao.Domain.Entities;
using FidelidadeTransacao.Domain.Enums;
using FidelidadeTransacao.Domain.Interfaces;

namespace FidelidadeTransacao.Infrastructure.Persistence.Repositories;

/// <summary>
/// Repositório de contas do Ledger usando Dapper.
///
/// CQRS:
/// - Operações de escrita (INSERT, UPDATE) e leituras dentro de transação
///   usam a conexão do UnitOfWork (Write — banco primário).
/// - Leituras simples sem transação usam IReadDbConnectionFactory (réplica).
///
/// PONTO CRÍTICO — RN02 (Lock Pessimista):
/// ObterComLockAsync usa WITH (UPDLOCK, ROWLOCK) — DEVE rodar no banco primário
/// dentro de uma transação ativa. Nunca direcionar para a réplica.
/// </summary>
public sealed class LedgerAccountRepository(
    UnitOfWork unitOfWork,
    IReadDbConnectionFactory readFactory) : ILedgerAccountRepository
{
    public async Task<LedgerAccount?> ObterComLockAsync(Guid accountId, CancellationToken ct = default)
    {
        // UPDLOCK + ROWLOCK: lock pessimista na linha da conta.
        // Executa SEMPRE no banco primário via UnitOfWork (Write connection).
        const string sql = """
            SELECT
                Id, CustomerId, Balance, Status, CriadoEm, CriadoPor
            FROM
                LedgerAccounts WITH (UPDLOCK, ROWLOCK)
            WHERE
                Id = @AccountId
            """;

        var row = await unitOfWork.Connection.QuerySingleOrDefaultAsync<LedgerAccountRow>(
            sql,
            new { AccountId = accountId },
            transaction: unitOfWork.CurrentTransaction);

        return row is null ? null : MapToDomain(row);
    }

    public async Task<LedgerAccount?> ObterPorIdAsync(Guid accountId, CancellationToken ct = default)
    {
        // Leitura simples — usa réplica (Read connection), sem lock, sem transação.
        const string sql = """
            SELECT Id, CustomerId, Balance, Status, CriadoEm, CriadoPor
            FROM LedgerAccounts
            WHERE Id = @AccountId
            """;

        using var connection = readFactory.CreateConnection();
        var row = await connection.QuerySingleOrDefaultAsync<LedgerAccountRow>(
            sql,
            new { AccountId = accountId });

        return row is null ? null : MapToDomain(row);
    }

    public async Task AdicionarAsync(LedgerAccount account, CancellationToken ct = default)
    {
        const string sql = """
            INSERT INTO LedgerAccounts (Id, CustomerId, Balance, Status, CriadoEm, CriadoPor)
            VALUES (@Id, @CustomerId, @Balance, @Status, @CriadoEm, @CriadoPor)
            """;

        await unitOfWork.Connection.ExecuteAsync(sql, new
        {
            account.Id,
            account.CustomerId,
            account.Balance,
            Status    = account.Status.ToString(),
            account.CriadoEm,
            account.CriadoPor
        }, transaction: unitOfWork.CurrentTransaction);
    }

    public async Task AtualizarSaldoAsync(Guid accountId, decimal novoSaldo, CancellationToken ct = default)
    {
        // UPDATE sempre no banco primário via UnitOfWork.
        const string sql = """
            UPDATE LedgerAccounts
            SET Balance = @NovoSaldo
            WHERE Id = @AccountId
            """;

        var affected = await unitOfWork.Connection.ExecuteAsync(sql,
            new { AccountId = accountId, NovoSaldo = novoSaldo },
            transaction: unitOfWork.CurrentTransaction);

        if (affected == 0)
            throw new InvalidOperationException(
                $"Falha ao atualizar saldo da conta '{accountId}'. Nenhuma linha afetada.");
    }

    // ── Mapeamento manual ────────────────────────────────────────────────────

    private static LedgerAccount MapToDomain(LedgerAccountRow row)
    {
        var account = (LedgerAccount)Activator.CreateInstance(typeof(LedgerAccount), nonPublic: true)!;

        SetProperty(account, nameof(LedgerAccount.Id), row.Id);
        SetProperty(account, nameof(LedgerAccount.CustomerId), row.CustomerId);
        SetProperty(account, nameof(LedgerAccount.Balance), row.Balance);
        SetProperty(account, nameof(LedgerAccount.Status), Enum.Parse<AccountStatus>(row.Status));
        SetProperty(account, nameof(LedgerAccount.CriadoEm), row.CriadoEm);
        SetProperty(account, nameof(LedgerAccount.CriadoPor), row.CriadoPor);

        return account;
    }

    private static void SetProperty(object obj, string propertyName, object value)
    {
        var prop = obj.GetType().GetProperty(propertyName)
            ?? throw new InvalidOperationException($"Propriedade '{propertyName}' não encontrada.");
        prop.SetValue(obj, value);
    }

    private sealed record LedgerAccountRow(
        Guid Id,
        Guid CustomerId,
        decimal Balance,
        string Status,
        DateTimeOffset CriadoEm,
        string CriadoPor);
}
