using FidelidadeTransacao.Application.Features.Ledger.Commands;
using FidelidadeTransacao.Domain.Entities;
using FidelidadeTransacao.Domain.Enums;
using FidelidadeTransacao.Domain.Exceptions;
using FidelidadeTransacao.Domain.Interfaces;
using FidelidadeTransacao.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FidelidadeTransacao.Application.Features.Ledger.Commands.RefundPoints;

/// <summary>
/// Handler do Command de Estorno de Pontos (Refund).
///
/// REGRAS ESPECÍFICAS DO ESTORNO:
/// - O lançamento original deve existir e pertencer à mesma conta (RN05)
/// - O amount do estorno não pode exceder o amount do lançamento original
/// - Um estorno de Debit → credita pontos de volta (saldo aumenta)
/// - Um estorno de Credit → debita pontos (saldo diminui, com validação RN01)
/// - O lançamento original NUNCA é modificado — novo lançamento compensatório (RN05)
/// </summary>
public sealed class RefundPointsCommandHandler(
    ILedgerAccountRepository accountRepository,
    ILedgerEntryRepository entryRepository,
    IIdempotencyRepository idempotencyRepository,
    IUnitOfWork unitOfWork,
    IPublisher publisher,
    ILogger<RefundPointsCommandHandler> logger) : IRequestHandler<RefundPointsCommand, LedgerOperationResult>
{
    public async Task<LedgerOperationResult> Handle(
        RefundPointsCommand request,
        CancellationToken cancellationToken)
    {
        // ── PASSO 1: Idempotência (RN03) ─────────────────────────────────────
        var existing = await idempotencyRepository.ObterPorChaveAsync(
            request.IdempotencyKey, cancellationToken);

        if (existing is not null)
        {
            logger.LogInformation(
                "[Ledger][Refund] Replay idempotente. Key: {Key} | CorrelationId: {CorrelationId}",
                MaskKey(request.IdempotencyKey), request.CorrelationId);

            var cached = JsonSerializer.Deserialize<LedgerOperationResult>(existing.ResponsePayload)!;
            return cached with { IsIdempotentReplay = true };
        }

        // ── PASSO 2: Transação com lock pessimista (RN02) ────────────────────
        await unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            // Lock na conta antes de qualquer leitura de saldo
            var account = await accountRepository.ObterComLockAsync(
                request.AccountId, cancellationToken);

            if (account is null)
                throw new KeyNotFoundException($"Conta '{request.AccountId}' não encontrada.");

            // ── PASSO 3: Valida lançamento original ───────────────────────────
            var originalEntry = await entryRepository.ObterPorIdAsync(
                request.OriginalTransactionId, cancellationToken);

            if (originalEntry is null)
                throw new DomainException(
                    $"Lançamento original '{request.OriginalTransactionId}' não encontrado.");

            if (originalEntry.AccountId != request.AccountId)
                throw new DomainException(
                    "O lançamento original não pertence à conta informada.");

            if (originalEntry.Type == EntryType.Refund)
                throw new DomainException(
                    "Não é possível estornar um lançamento que já é um estorno.");

            if (request.Amount > originalEntry.Amount)
                throw new DomainException(
                    $"O valor do estorno ({request.Amount:N0}) não pode exceder " +
                    $"o valor original ({originalEntry.Amount:N0}).");

            var metadata = StatementMetadata.Create(
                request.PartnerId, request.ProductName, request.Description);

            // ── PASSO 4: Aplica efeito financeiro do estorno ──────────────────
            // Estorno de Debit → devolve pontos (crédito compensatório)
            // Estorno de Credit → remove pontos indevidos (débito compensatório, valida RN01)
            if (originalEntry.Type == EntryType.Debit)
                account.AplicarCredito(request.Amount);
            else
                account.AplicarDebito(request.Amount); // Pode lançar InsufficientBalanceException

            // ── PASSO 5: Cria lançamento de estorno (RN05 — append-only) ─────
            var refundEntry = LedgerEntry.CriarEstorno(
                accountId:          request.AccountId,
                originalEntryId:    request.OriginalTransactionId,
                amount:             request.Amount,
                occurrenceDate:     request.OccurrenceDate,
                idempotencyKey:     request.IdempotencyKey,
                correlationId:      request.CorrelationId,
                reason:             request.Reason,
                statementMetadata:  metadata);

            await entryRepository.InserirAsync(refundEntry, cancellationToken);
            await accountRepository.AtualizarSaldoAsync(
                account.Id, account.Balance, cancellationToken);

            // ── PASSO 6: Idempotência ─────────────────────────────────────────
            var result = new LedgerOperationResult(
                TransactionId: refundEntry.Id,
                AccountId:     account.Id,
                Amount:        request.Amount,
                BalanceAfter:  account.Balance,
                EntryType:     "Refund",
                ProcessedAt:   DateTimeOffset.UtcNow,
                CorrelationId: request.CorrelationId);

            await idempotencyRepository.InserirAsync(
                IdempotencyRecord.Criar(request.IdempotencyKey, refundEntry.Id,
                    JsonSerializer.Serialize(result)),
                cancellationToken);

            // ── PASSO 7: Commit ───────────────────────────────────────────────
            await unitOfWork.CommitAsync(cancellationToken);

            logger.LogInformation(
                "[Ledger][Refund] Estorno {TransactionId} confirmado. " +
                "Original: {OriginalId} | Conta: {AccountId} | Pontos: {Amount} | " +
                "Saldo: {Balance} | CorrelationId: {CorrelationId}",
                refundEntry.Id, request.OriginalTransactionId,
                account.Id, request.Amount, account.Balance, request.CorrelationId);

            // ── PASSO 8: Publica eventos APÓS commit ──────────────────────────
            foreach (var domainEvent in refundEntry.DomainEvents)
                await publisher.Publish(domainEvent, cancellationToken);

            refundEntry.ClearDomainEvents();

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
