using FidelidadeTransacao.Application.Features.Ledger.Commands;
using FidelidadeTransacao.Domain.Entities;
using FidelidadeTransacao.Domain.Exceptions;
using FidelidadeTransacao.Domain.Interfaces;
using FidelidadeTransacao.Domain.ValueObjects;
using MediatR;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace FidelidadeTransacao.Application.Features.Ledger.Commands.CreditPoints;

/// <summary>
/// Handler do Command de Crédito de Pontos.
///
/// FLUXO COMPLETO (RN01 + RN02 + RN03 + RN04 + RN05):
///
/// 1. [RN03] Verifica idempotência ANTES de qualquer lock
///    → Se já processado: retorna resposta original sem reprocessar
///
/// 2. [RN02] Inicia transação SQL e aplica UPDLOCK na conta
///    → Garante serialização de operações concorrentes na mesma conta
///
/// 3. [RN01] Valida regras de negócio (conta ativa, amount > 0)
///
/// 4. [RN05] Cria LedgerEntry (append-only) e atualiza saldo atomicamente
///
/// 5. [RN03] Persiste registro de idempotência na mesma transação
///
/// 6. Commit da transação SQL
///
/// 7. Publica Domain Events no Service Bus (APÓS commit — consistência eventual)
/// </summary>
public sealed class CreditPointsCommandHandler(
    ILedgerAccountRepository accountRepository,
    ILedgerEntryRepository entryRepository,
    IIdempotencyRepository idempotencyRepository,
    IUnitOfWork unitOfWork,
    IPublisher publisher,
    ILogger<CreditPointsCommandHandler> logger) : IRequestHandler<CreditPointsCommand, LedgerOperationResult>
{
    public async Task<LedgerOperationResult> Handle(
        CreditPointsCommand request,
        CancellationToken cancellationToken)
    {
        // ── PASSO 1: Verificação de Idempotência (RN03) ──────────────────────
        // Verificação ANTES do lock para evitar overhead desnecessário
        var existing = await idempotencyRepository.ObterPorChaveAsync(
            request.IdempotencyKey, cancellationToken);

        if (existing is not null)
        {
            logger.LogInformation(
                "[Ledger][Credit] Replay idempotente detectado. Key: {Key} | CorrelationId: {CorrelationId}",
                MaskKey(request.IdempotencyKey),
                request.CorrelationId);

            // Desserializa e retorna a resposta original — cliente recebe HTTP 200 idêntico
            var cached = JsonSerializer.Deserialize<LedgerOperationResult>(existing.ResponsePayload)!;
            return cached with { IsIdempotentReplay = true };
        }

        // ── PASSO 2: Inicia transação com lock pessimista (RN02) ─────────────
        await unitOfWork.BeginTransactionAsync(cancellationToken);

        try
        {
            // UPDLOCK + ROWLOCK aplicado aqui — bloqueia a linha da conta
            // Requisições concorrentes para a mesma conta ficam em fila no SQL Server
            var account = await accountRepository.ObterComLockAsync(
                request.AccountId, cancellationToken);

            if (account is null)
                throw new KeyNotFoundException($"Conta '{request.AccountId}' não encontrada.");

            // ── PASSO 3: Validação de domínio (RN01) ─────────────────────────
            var metadata = StatementMetadata.Create(
                request.PartnerId,
                request.ProductName,
                request.Description);

            account.ValidarParaCredito(request.Amount);

            // ── PASSO 4: Cria lançamento imutável (RN05) ─────────────────────
            var entry = LedgerEntry.CriarCredito(
                accountId:        request.AccountId,
                amount:           request.Amount,
                occurrenceDate:   request.OccurrenceDate,
                idempotencyKey:   request.IdempotencyKey,
                correlationId:    request.CorrelationId,
                statementMetadata: metadata);

            // Aplica crédito ao saldo em memória
            account.AplicarCredito(request.Amount);

            // INSERT do lançamento (append-only)
            await entryRepository.InserirAsync(entry, cancellationToken);

            // UPDATE do saldo (único UPDATE permitido, dentro do lock)
            await accountRepository.AtualizarSaldoAsync(
                account.Id, account.Balance, cancellationToken);

            // ── PASSO 5: Persiste idempotência (RN03) ────────────────────────
            var result = new LedgerOperationResult(
                TransactionId: entry.Id,
                AccountId:     account.Id,
                Amount:        request.Amount,
                BalanceAfter:  account.Balance,
                EntryType:     "Credit",
                ProcessedAt:   DateTimeOffset.UtcNow,
                CorrelationId: request.CorrelationId);

            var idempotencyRecord = IdempotencyRecord.Criar(
                request.IdempotencyKey,
                entry.Id,
                JsonSerializer.Serialize(result));

            await idempotencyRepository.InserirAsync(idempotencyRecord, cancellationToken);

            // ── PASSO 6: Commit atômico ──────────────────────────────────────
            await unitOfWork.CommitAsync(cancellationToken);

            logger.LogInformation(
                "[Ledger][Credit] Transação {TransactionId} confirmada. " +
                "Conta: {AccountId} | Pontos: {Amount} | Saldo: {Balance} | CorrelationId: {CorrelationId}",
                entry.Id,
                account.Id,
                request.Amount,
                account.Balance,
                request.CorrelationId);

            // ── PASSO 7: Publica Domain Events APÓS commit (RN04) ────────────
            // Se o Service Bus falhar aqui, o lançamento já está no banco.
            // Em produção, implementar Outbox Pattern para garantia de entrega.
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

    // Mascara a chave de idempotência nos logs para evitar exposição desnecessária
    private static string MaskKey(string key)
        => key.Length > 8 ? $"{key[..8]}***" : "***";
}
