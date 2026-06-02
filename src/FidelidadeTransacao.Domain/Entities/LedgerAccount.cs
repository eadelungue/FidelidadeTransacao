using FidelidadeTransacao.Domain.Common;
using FidelidadeTransacao.Domain.Enums;
using FidelidadeTransacao.Domain.Exceptions;

namespace FidelidadeTransacao.Domain.Entities;

/// <summary>
/// Entidade que representa uma conta (carteira) de pontos de fidelidade.
/// Um cliente pode ter múltiplas contas ("bolsos").
///
/// DECISÃO ARQUITETURAL — Saldo como campo desnormalizado:
/// O saldo (Balance) é mantido como campo calculado na tabela LedgerAccounts.
/// Alternativa seria calcular SUM(entries) a cada operação, mas isso seria
/// O(n) e inviável em contas com milhões de lançamentos.
/// O saldo é atualizado atomicamente dentro da mesma transação SQL que insere
/// o lançamento, usando UPDLOCK para garantir a RN02 (sem double-spending).
/// </summary>
public sealed class LedgerAccount : AuditableEntity
{
    private LedgerAccount() { }

    public Guid CustomerId { get; private set; }
    public decimal Balance { get; private set; }
    public AccountStatus Status { get; private set; }

    public static LedgerAccount Criar(Guid customerId, string criadoPor)
    {
        if (customerId == Guid.Empty)
            throw new DomainException("CustomerId não pode ser vazio.");

        return new LedgerAccount
        {
            CustomerId = customerId,
            Balance    = 0m,
            Status     = AccountStatus.Active,
            CriadoEm  = DateTimeOffset.UtcNow,
            CriadoPor = criadoPor
        };
    }

    /// <summary>
    /// Valida se a conta pode receber um crédito.
    /// A atualização real do saldo ocorre via SQL atômico no repositório.
    /// </summary>
    public void ValidarParaCredito(decimal amount)
    {
        ValidarContaAtiva();

        if (amount <= 0)
            throw new DomainException("O valor do crédito deve ser maior que zero.");
    }

    /// <summary>
    /// Valida se a conta pode realizar um débito.
    /// RN01: Saldo Intransponível — nunca permite saldo negativo.
    /// Esta validação ocorre APÓS o lock pessimista no banco (RN02),
    /// garantindo que o saldo lido é o saldo real no momento do lock.
    /// </summary>
    public void ValidarParaDebito(decimal amount)
    {
        ValidarContaAtiva();

        if (amount <= 0)
            throw new DomainException("O valor do débito deve ser maior que zero.");

        if (Balance < amount)
            throw new InsufficientBalanceException(Balance, amount);
    }

    /// <summary>
    /// Aplica o crédito ao saldo em memória (após lock e validação).
    /// O valor real é persistido via SQL UPDATE atômico.
    /// </summary>
    public void AplicarCredito(decimal amount)
    {
        ValidarParaCredito(amount);
        Balance += amount;
    }

    /// <summary>
    /// Aplica o débito ao saldo em memória (após lock e validação).
    /// </summary>
    public void AplicarDebito(decimal amount)
    {
        ValidarParaDebito(amount);
        Balance -= amount;
    }

    private void ValidarContaAtiva()
    {
        if (Status != AccountStatus.Active)
            throw new DomainException($"A conta está '{Status}' e não pode processar transações.");
    }
}
