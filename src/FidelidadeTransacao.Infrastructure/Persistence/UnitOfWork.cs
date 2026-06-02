using FidelidadeTransacao.Domain.Interfaces;
using System.Data;

namespace FidelidadeTransacao.Infrastructure.Persistence;

/// <summary>
/// Implementação do Unit of Work com controle explícito de transação ADO.NET.
///
/// DESIGN CRÍTICO:
/// - Uma única IDbConnection é compartilhada entre todos os repositórios no mesmo scope.
/// - A IDbTransaction é passada para os repositórios via propriedade CurrentTransaction.
/// - Isso garante que INSERT de lançamento, UPDATE de saldo e INSERT de idempotência
///   ocorram na MESMA transação SQL — atomicidade total (ACID).
///
/// REGISTRO NO DI: Scoped — uma instância por request HTTP.
/// Os repositórios também devem ser Scoped e receber este UoW.
/// </summary>
public sealed class UnitOfWork(IWriteDbConnectionFactory connectionFactory) : IUnitOfWork
{
    private IDbConnection? _connection;
    private IDbTransaction? _transaction;
    private bool _disposed;

    /// <summary>
    /// Conexão ativa compartilhada com os repositórios.
    /// Criada no BeginTransaction e reutilizada até o Dispose.
    /// </summary>
    public IDbConnection Connection
    {
        get
        {
            if (_connection is null || _connection.State != ConnectionState.Open)
                _connection = connectionFactory.CreateConnection();
            return _connection;
        }
    }

    /// <summary>
    /// Transação ativa. Null fora de um bloco BeginTransaction/Commit.
    /// Os repositórios usam esta transação em seus comandos Dapper.
    /// </summary>
    public IDbTransaction? CurrentTransaction => _transaction;

    public Task BeginTransactionAsync(CancellationToken ct = default)
    {
        if (_transaction is not null)
            throw new InvalidOperationException("Já existe uma transação ativa.");

        // IsolationLevel.ReadCommitted é o padrão do SQL Server.
        // O UPDLOCK no SELECT garante o lock pessimista independente do isolation level.
        _transaction = Connection.BeginTransaction(IsolationLevel.ReadCommitted);
        return Task.CompletedTask;
    }

    public Task CommitAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            throw new InvalidOperationException("Nenhuma transação ativa para commitar.");

        _transaction.Commit();
        _transaction.Dispose();
        _transaction = null;
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken ct = default)
    {
        if (_transaction is null)
            return Task.CompletedTask;

        _transaction.Rollback();
        _transaction.Dispose();
        _transaction = null;
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;

        if (_transaction is not null)
        {
            // Rollback automático se o Dispose for chamado sem Commit (ex: exceção não tratada)
            await RollbackAsync();
        }

        _connection?.Dispose();
        _disposed = true;
    }
}
