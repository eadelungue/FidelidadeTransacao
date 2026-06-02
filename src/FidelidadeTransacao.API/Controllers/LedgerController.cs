using FidelidadeTransacao.Application.Features.Ledger.Commands;
using FidelidadeTransacao.Application.Features.Ledger.Commands.CreditPoints;
using FidelidadeTransacao.Application.Features.Ledger.Commands.DebitPoints;
using FidelidadeTransacao.Application.Features.Ledger.Commands.RefundPoints;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace FidelidadeTransacao.API.Controllers;

/// <summary>
/// Controller do Motor Transacional (Ledger).
/// Responsabilidade única: receber requests HTTP, mapear para Commands e retornar respostas.
/// Nenhuma lógica de negócio aqui — apenas orquestração HTTP (SRP).
///
/// RN04 — Correlation ID:
/// O CorrelationId é lido do header X-Correlation-Id e propagado para o Command.
/// Se não informado, um novo GUID é gerado para garantir rastreabilidade.
/// </summary>
[ApiController]
[Route("api/v1/ledger")]
[Produces("application/json")]
[Authorize(Policy = "LedgerWrite")]
[EnableRateLimiting("ledger-write")]
public sealed class LedgerController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Acumula pontos em uma conta (Credit).
    /// </summary>
    [HttpPost("credit")]
    [ProducesResponseType(typeof(LedgerOperationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Credit(
        [FromBody] CreditPointsRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = ObterOuGerarCorrelationId();

        var command = new CreditPointsCommand(
            AccountId:      request.AccountId,
            Amount:         request.Amount,
            OccurrenceDate: request.OccurrenceDate,
            IdempotencyKey: request.IdempotencyKey,
            CorrelationId:  correlationId,
            PartnerId:      request.StatementMetadata.PartnerId,
            ProductName:    request.StatementMetadata.ProductName,
            Description:    request.StatementMetadata.Description);

        var result = await mediator.Send(command, cancellationToken);

        // HTTP 200 para ambos: novo processamento e replay idempotente (RN03)
        return Ok(result);
    }

    /// <summary>
    /// Resgata pontos de uma conta (Debit).
    /// </summary>
    [HttpPost("debit")]
    [ProducesResponseType(typeof(LedgerOperationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Debit(
        [FromBody] DebitPointsRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = ObterOuGerarCorrelationId();

        var command = new DebitPointsCommand(
            AccountId:      request.AccountId,
            Amount:         request.Amount,
            OccurrenceDate: request.OccurrenceDate,
            ReferenceId:    request.ReferenceId,
            IdempotencyKey: request.IdempotencyKey,
            CorrelationId:  correlationId,
            PartnerId:      request.StatementMetadata.PartnerId,
            ProductName:    request.StatementMetadata.ProductName,
            Description:    request.StatementMetadata.Description);

        var result = await mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Estorna pontos de uma transação anterior (Refund).
    /// O lançamento original não é alterado — novo lançamento compensatório (RN05).
    /// </summary>
    [HttpPost("refund")]
    [ProducesResponseType(typeof(LedgerOperationResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Refund(
        [FromBody] RefundPointsRequest request,
        CancellationToken cancellationToken)
    {
        var correlationId = ObterOuGerarCorrelationId();

        var command = new RefundPointsCommand(
            AccountId:             request.AccountId,
            OriginalTransactionId: request.OriginalTransactionId,
            Amount:                request.Amount,
            OccurrenceDate:        request.OccurrenceDate,
            Reason:                request.Reason,
            IdempotencyKey:        request.IdempotencyKey,
            CorrelationId:         correlationId,
            PartnerId:             request.StatementMetadata.PartnerId,
            ProductName:           request.StatementMetadata.ProductName,
            Description:           request.StatementMetadata.Description);

        var result = await mediator.Send(command, cancellationToken);
        return Ok(result);
    }

    /// <summary>
    /// Lê o X-Correlation-Id do header ou gera um novo (RN04).
    /// </summary>
    private string ObterOuGerarCorrelationId()
    {
        if (Request.Headers.TryGetValue("X-Correlation-Id", out var value)
            && !string.IsNullOrWhiteSpace(value))
            return value.ToString();

        return Guid.NewGuid().ToString();
    }
}

// ── Request DTOs ─────────────────────────────────────────────────────────────
// Separados do Command para desacoplar o contrato HTTP da Application layer

public sealed record StatementMetadataRequest(
    string PartnerId,
    string ProductName,
    string? Description);

public sealed record CreditPointsRequest(
    Guid AccountId,
    decimal Amount,
    DateTimeOffset OccurrenceDate,
    string IdempotencyKey,
    StatementMetadataRequest StatementMetadata);

public sealed record DebitPointsRequest(
    Guid AccountId,
    decimal Amount,
    DateTimeOffset OccurrenceDate,
    string ReferenceId,
    string IdempotencyKey,
    StatementMetadataRequest StatementMetadata);

public sealed record RefundPointsRequest(
    Guid AccountId,
    Guid OriginalTransactionId,
    decimal Amount,
    DateTimeOffset OccurrenceDate,
    string Reason,
    string IdempotencyKey,
    StatementMetadataRequest StatementMetadata);
