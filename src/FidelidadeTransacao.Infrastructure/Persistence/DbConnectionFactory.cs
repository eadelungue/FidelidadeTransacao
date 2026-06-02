using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;
using System.Data;

namespace FidelidadeTransacao.Infrastructure.Persistence;

/// <summary>
/// Factories de conexão SQL Server separadas para leitura e escrita — padrão CQRS.
///
/// CQRS:
/// - IWriteDbConnectionFactory → conexão com o banco primário (Commands / UnitOfWork)
/// - IReadDbConnectionFactory  → conexão com a réplica somente-leitura (Queries)
///
/// Em desenvolvimento (appsettings.Development.json) ambas apontam para o mesmo banco.
/// Em staging/produção, WriteConnection aponta para o primário e ReadConnection para
/// a réplica com ApplicationIntent=ReadOnly — basta configurar as connection strings
/// no pipeline de deploy (ex: variáveis de ambiente ou Azure Key Vault).
/// </summary>
public interface IWriteDbConnectionFactory
{
    IDbConnection CreateConnection();
}

public interface IReadDbConnectionFactory
{
    IDbConnection CreateConnection();
}

/// <summary>
/// Conexão de escrita — banco primário.
/// Usada pelo UnitOfWork e qualquer operação que precise de transação ou lock.
/// Connection string: "WriteConnection"
/// </summary>
public sealed class WriteConnectionFactory(IConfiguration configuration) : IWriteDbConnectionFactory
{
    private readonly string _connectionString =
        configuration.GetConnectionString("WriteConnection")
        ?? throw new InvalidOperationException(
            "Connection string 'WriteConnection' não configurada. " +
            "Verifique o appsettings do ambiente.");

    public IDbConnection CreateConnection()
    {
        var connection = new SqlConnection(_connectionString);
        connection.Open();
        return connection;
    }
}

/// <summary>
/// Conexão de leitura — réplica somente-leitura.
/// Usada por queries que não participam de transações (sem UPDLOCK, sem BEGIN TRAN).
/// Em produção, configure com ApplicationIntent=ReadOnly para direcionar ao secondary.
/// Connection string: "ReadConnection"
/// </summary>
public sealed class ReadConnectionFactory(IConfiguration configuration) : IReadDbConnectionFactory
{
    private readonly string _connectionString =
        configuration.GetConnectionString("ReadConnection")
        ?? throw new InvalidOperationException(
            "Connection string 'ReadConnection' não configurada. " +
            "Verifique o appsettings do ambiente.");

    public IDbConnection CreateConnection()
    {
        var connection = new SqlConnection(_connectionString);
        connection.Open();
        return connection;
    }
}
