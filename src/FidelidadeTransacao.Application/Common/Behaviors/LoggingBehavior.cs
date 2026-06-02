using MediatR;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace FidelidadeTransacao.Application.Common.Behaviors;

/// <summary>
/// Pipeline Behavior que loga duração e resultado de cada Command/Query.
/// SEGURANÇA: Nunca loga o conteúdo do request para evitar vazamento de PII
/// (valores financeiros, IDs de clientes) em logs — OWASP API Security.
/// Apenas o nome do tipo e a duração são registrados em nível Info.
/// </summary>
public sealed class LoggingBehavior<TRequest, TResponse>(
    ILogger<LoggingBehavior<TRequest, TResponse>> logger) : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    public async Task<TResponse> Handle(
        TRequest request,
        RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        var requestName = typeof(TRequest).Name;
        var sw = Stopwatch.StartNew();

        logger.LogInformation("[Ledger] Iniciando {RequestName}", requestName);

        try
        {
            var response = await next();
            sw.Stop();
            logger.LogInformation("[Ledger] Concluído {RequestName} em {ElapsedMs}ms",
                requestName, sw.ElapsedMilliseconds);
            return response;
        }
        catch (Exception ex)
        {
            sw.Stop();
            logger.LogError(ex, "[Ledger] Falha em {RequestName} após {ElapsedMs}ms",
                requestName, sw.ElapsedMilliseconds);
            throw;
        }
    }
}
