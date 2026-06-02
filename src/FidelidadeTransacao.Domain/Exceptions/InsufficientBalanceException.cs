namespace FidelidadeTransacao.Domain.Exceptions;

/// <summary>
/// RN01 — Saldo Intransponível.
/// Lançada quando uma operação de débito excede o saldo disponível.
/// Mapeada para HTTP 422 com código de erro específico para o cliente.
/// </summary>
public sealed class InsufficientBalanceException(decimal saldoAtual, decimal valorSolicitado)
    : DomainException(
        $"Saldo insuficiente. Saldo disponível: {saldoAtual:N0} pontos. " +
        $"Valor solicitado: {valorSolicitado:N0} pontos.");
