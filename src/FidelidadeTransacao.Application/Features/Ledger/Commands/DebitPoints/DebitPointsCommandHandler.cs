using FidelidadeTransacao.Application.Features.Ledger.Commands;
using FidelidadeTransacao.Domain.Entities;
using FidelidadeTransacao.Domain.Interfaces;
using FidelidadeTransacao.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FidelidadeTransacao.Application.Features.Ledger.Commands.DebitPoints;

/// <summary>
/// Handler do Command de Débito de Pontos (Resgate).
///
/// PONTO CRÍTICO — RN01 + RN02 (Double-Spending Prevention):
/// O lock pessimista (UPDLOCK) é aplicado no ObterComLockAsync.
/// Isso garante que se dois resgates chegarem simultaneamente para a mesma conta:
///   - Requisição A: obtém o lock, lê saldo=1000, debita 800, saldo=200, commit
///   - Requisição B: aguarda o lock, lê saldo=200 (atualizado), tenta debitar 800 → FALHA (RN01)
/// Sem o lock, ambas leriam saldo=1000 e ambas passariam na validação → double-spending.
/// </summary>
public sealed class DebitPointsCommandHandler(
    ILedgerAccountRepository accountRepository,
    ILedgerEntryRepository entryRepository,
    IIdempotencyRepository idempotencyRepository,
    IUnitOfWork unitOfWork,
    IPublisher publisher,
    ILogger<DebitPointsCommandHandler> logger) : IRequestHandler<DebitPointsCommand, LedgerOperationResult>
{
    public async Task<LedgerOperationResult> Handle(
        DebitPointsCommand request,
        CancellationToken cancellationToken)
    {
        // ── PASSO 1: Idempotência (RN03) ─────────────────────────────────────
        var existing = await idempotencyRepository.ObterPorChaveAsync(
            request.IdempotencyKey, cancellationToken);

        if (existing is not null)
        {
            logger.LogInformation(
                "[Ledger][Debit] Replay idempotente. Key: {Key} | CorrelationId: {CorrelationId}",
                MaskKey(request.IdempotencyKey), request.CorrelationId);

            var cached = JsonSerializer.Deserialize<LedgerOperationResult>(existing.ResponsePayload)!;
            return cached with { IsIdempotentReplay = true };
        }

        // ── PASSO 2: Transação com lock pessimista (RN02) ────────────────────
        await unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            // UPDLOCK aplicado aqui — serializa operações concorrentes na conta
            var account = await accountRepository.ObterComLockAsync(
                request.AccountId, cancellationToken);

            if (account is null)
                throw new KeyNotFoundException($"Conta '{request.AccountId}' não encontrada.");

            var metadata = StatementMetadata.Create(
                request.PartnerId, request.ProductName, request.Description);

            // ── PASSO 3: Validação RN01 (saldo suficiente) ───────────────────
            // ValidarParaDebito lança InsufficientBalanceException se saldo < amount
            account.ValidarParaDebito(request.Amount);

            // ── PASSO 4: Lançamento imutável (RN05) ──────────────────────────
            var entry = LedgerEntry.CriarDebito(
                accountId:         request.AccountId,
                amount:            request.Amount,
                occurrenceDate:    request.OccurrenceDate,
                idempotencyKey:    request.IdempotencyKey,
                correlationId:     request.CorrelationId,
                referenceId:       request.ReferenceId,
                statementMetadata: metadata);

            account.AplicarDebito(request.Amount);

            await entryRepository.InserirAsync(entry, cancellationToken);
            await accountRepository.AtualizarSaldoAsync(
                account.Id, account.Balance, cancellationToken);

            // ── PASSO 5: Idempotência ─────────────────────────────────────────
            var result = new LedgerOperationResult(
                TransactionId: entry.Id,
                AccountId:     account.Id,
                Amount:        request.Amount,
                BalanceAfter:  account.Balance,
                EntryType:     "Debit",
                ProcessedAt:   DateTimeOffset.UtcNow,
                CorrelationId: request.CorrelationId);

            await idempotencyRepository.InserirAsync(
                IdempotencyRecord.Criar(request.IdempotencyKey, entry.Id,
                    JsonSerializer.Serialize(result)),
                cancellationToken);

            // ── PASSO 6: Commit ───────────────────────────────────────────────
            await unitOfWork.CommitAsync(cancellationToken);

            logger.LogInformation(
                "[Ledger][Debit] Transação {TransactionId} confirmada. " +
                "Conta: {AccountId} | Pontos: -{Amount} | Saldo: {Balance} | CorrelationId: {CorrelationId}",
                entry.Id, account.Id, request.Amount, account.Balance, request.CorrelationId);

            // ── PASSO 7: Publica eventos APÓS commit ──────────────────────────
            foreach (var domainEvent in entry.DomainEvents)
                await publisher.Publish(domainEvent, cancellationToken);

            entry.ClearDomainEvents();

            return result;
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static string MaskKey(string key)
        => key.Length > 8 ? $"{key[..8]}***" : "***";
}
