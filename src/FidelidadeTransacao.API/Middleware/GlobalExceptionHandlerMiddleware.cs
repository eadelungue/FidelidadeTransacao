using FidelidadeTransacao.Application.Common.Exceptions;
using FidelidadeTransacao.Domain.Exceptions;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace FidelidadeTransacao.API.Middleware;

/// <summary>
/// Middleware de tratamento global de exceções.
///
/// SEGURANÇA (OWASP — Security Misconfiguration):
/// - Stack traces NUNCA são enviados ao cliente em produção
/// - Exceções de domínio têm mensagens seguras para exposição
/// - Exceções de infraestrutura retornam mensagem genérica
/// - TraceId é incluído para correlação com logs internos
/// - Formato RFC 7807 (Problem Details) para respostas padronizadas
/// </summary>
public sealed class GlobalExceptionHandlerMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionHandlerMiddleware> logger,
    IHostEnvironment environment)
{
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await next(context);
        }
        catch (Exception ex)
        {
            await HandleAsync(context, ex);
        }
    }

    private async Task HandleAsync(HttpContext context, Exception exception)
    {
        var traceId = context.TraceIdentifier;

        // Log completo internamente — nunca enviado ao cliente
        logger.LogError(exception,
            "[Ledger] Exceção não tratada. TraceId: {TraceId} | Path: {Path}",
            traceId, context.Request.Path);

        var (status, problem) = exception switch
        {
            // HTTP 400 — Erros de validação FluentValidation
            ValidationException vex => (400, (ProblemDetails)new ValidationProblemDetails(vex.Errors)
            {
                Type     = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                Title    = "Erro de validação",
                Status   = 400,
                Detail   = "Um ou mais campos possuem valores inválidos.",
                Instance = context.Request.Path
            }),

            // HTTP 422 — Saldo insuficiente (RN01)
            InsufficientBalanceException ibex => (422, new ProblemDetails
            {
                Type     = "https://tools.ietf.org/html/rfc4918#section-11.2",
                Title    = "Saldo insuficiente",
                Status   = 422,
                Detail   = ibex.Message, // Mensagem de domínio é segura
                Instance = context.Request.Path
            }),

            // HTTP 422 — Outras violações de regras de negócio
            DomainException dex => (422, new ProblemDetails
            {
                Type     = "https://tools.ietf.org/html/rfc4918#section-11.2",
                Title    = "Regra de negócio violada",
                Status   = 422,
                Detail   = dex.Message,
                Instance = context.Request.Path
            }),

            // HTTP 404 — Recurso não encontrado
            KeyNotFoundException => (404, new ProblemDetails
            {
                Type     = "https://tools.ietf.org/html/rfc7231#section-6.5.4",
                Title    = "Recurso não encontrado",
                Status   = 404,
                Detail   = "O recurso solicitado não foi encontrado.",
                Instance = context.Request.Path
            }),

            // HTTP 499 — Cliente cancelou a requisição
            OperationCanceledException => (499, new ProblemDetails
            {
                Type     = "about:blank",
                Title    = "Requisição cancelada",
                Status   = 499,
                Detail   = "A requisição foi cancelada.",
                Instance = context.Request.Path
            }),

            // HTTP 500 — Qualquer outra exceção (nunca expõe detalhes em produção)
            _ => (500, new ProblemDetails
            {
                Type     = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                Title    = "Erro interno do servidor",
                Status   = 500,
                Detail   = environment.IsDevelopment()
                    ? exception.Message
                    : "Ocorreu um erro interno. Use o TraceId para acionar o suporte.",
                Instance = context.Request.Path
            })
        };

        // TraceId para correlação com logs — seguro para expor
        problem.Extensions["traceId"] = traceId;

        context.Response.StatusCode  = status;
        context.Response.ContentType = "application/problem+json";

        await context.Response.WriteAsync(
            JsonSerializer.Serialize(problem, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            }));
    }
}
