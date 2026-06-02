using FidelidadeTransacao.Application.Features.Ledger.Commands;
using FidelidadeTransacao.Application.Features.Ledger.Commands.CreditPoints;
using FidelidadeTransacao.Domain.Entities;
using FidelidadeTransacao.Domain.Interfaces;
using FidelidadeTransacao.Tests.Unit.Helpers;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FidelidadeTransacao.Tests.Unit.Features.Ledger;

/// <summary>
/// Testes unitários para o CreditPointsCommandHandler.
/// Todos os colaboradores externos (repositórios, UoW, publisher) são mockados.
/// Nenhuma conexão com banco de dados ou Service Bus é necessária.
/// </summary>
public sealed class CreditPointsCommandHandlerTests
{
    // ── Mocks ────────────────────────────────────────────────────────────────
    private readonly Mock<ILedgerAccountRepository> _accountRepoMock  = new();
    private readonly Mock<ILedgerEntryRepository>   _entryRepoMock    = new();
    private readonly Mock<IIdempotencyRepository>   _idempotencyMock  = new();
    private readonly Mock<IUnitOfWork>              _unitOfWorkMock   = new();
    private readonly Mock<IPublisher>               _publisherMock    = new();

    private readonly CreditPointsCommandHandler _handler;

    public CreditPointsCommandHandlerTests()
    {
        _handler = new CreditPointsCommandHandler(
            _accountRepoMock.Object,
            _entryRepoMock.Object,
            _idempotencyMock.Object,
            _unitOfWorkMock.Object,
            _publisherMock.Object,
            NullLogger<CreditPointsCommandHandler>.Instance);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static CreditPointsCommand CriarCommand(
        Guid? accountId      = null,
        decimal amount       = 500m,
        string? idempotencyKey = null) => new(
            AccountId:      accountId ?? Guid.NewGuid(),
            Amount:         amount,
            OccurrenceDate: DateTimeOffset.UtcNow.AddMinutes(-5),
            IdempotencyKey: idempotencyKey ?? $"key-{Guid.NewGuid()}",
            CorrelationId:  $"corr-{Guid.NewGuid()}",
            PartnerId:      "MAGALU",
            ProductName:    "iPhone 15 Pro",
            Description:    "Compra aprovada");

    // ════════════════════════════════════════════════════════════════════════
    // CENÁRIO 1: Crédito bem-sucedido
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Handle_DeveRetornarResultadoCorreto_QuandoCreditoValido()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var saldoInicial = 1000m;
        var valorCredito = 500m;

        var account = new LedgerAccountBuilder()
            .ComId(accountId)
            .ComSaldo(saldoInicial)
            .Build();

        var command = CriarCommand(accountId: accountId, amount: valorCredito);

        // Sem registro de idempotência existente
        _idempotencyMock
            .Setup(r => r.ObterPorChaveAsync(command.IdempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        // Conta encontrada com lock
        _accountRepoMock
            .Setup(r => r.ObterComLockAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.TransactionId.Should().NotBeEmpty();
        result.AccountId.Should().Be(accountId);
        result.Amount.Should().Be(valorCredito);
        result.BalanceAfter.Should().Be(saldoInicial + valorCredito); // 1500
        result.EntryType.Should().Be("Credit");
        result.CorrelationId.Should().Be(command.CorrelationId);
        result.IsIdempotentReplay.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_DeveExecutarFluxoCompleto_NaOrdemCorreta()
    {
        // Arrange — verifica a ordem das operações (RN02: lock antes de tudo)
        var callOrder = new List<string>();
        var accountId = Guid.NewGuid();
        var account   = new LedgerAccountBuilder().ComId(accountId).Build();
        var command   = CriarCommand(accountId: accountId);

        _idempotencyMock
            .Setup(r => r.ObterPorChaveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        _unitOfWorkMock
            .Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("BeginTransaction"))
            .Returns(Task.CompletedTask);

        _accountRepoMock
            .Setup(r => r.ObterComLockAsync(accountId, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("ObterComLock"))
            .ReturnsAsync(account);

        _entryRepoMock
            .Setup(r => r.InserirAsync(It.IsAny<LedgerEntry>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("InserirEntry"))
            .Returns(Task.CompletedTask);

        _accountRepoMock
            .Setup(r => r.AtualizarSaldoAsync(It.IsAny<Guid>(), It.IsAny<decimal>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("AtualizarSaldo"))
            .Returns(Task.CompletedTask);

        _idempotencyMock
            .Setup(r => r.InserirAsync(It.IsAny<IdempotencyRecord>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("InserirIdempotency"))
            .Returns(Task.CompletedTask);

        _unitOfWorkMock
            .Setup(u => u.CommitAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("Commit"))
            .Returns(Task.CompletedTask);

        _publisherMock
            .Setup(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("PublishEvent"))
            .Returns(Task.CompletedTask);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — ordem crítica para garantir RN02 e consistência
        callOrder.Should().ContainInOrder(
            "BeginTransaction",   // Lock iniciado
            "ObterComLock",       // Lock aplicado na conta
            "InserirEntry",       // Lançamento inserido
            "AtualizarSaldo",     // Saldo atualizado
            "InserirIdempotency", // Idempotência registrada
            "Commit",             // Tudo commitado atomicamente
            "PublishEvent");      // Evento publicado APÓS commit
    }

    // ════════════════════════════════════════════════════════════════════════
    // CENÁRIO 2: Idempotência (RN03)
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Handle_DeveRetornarRespostaOriginal_QuandoIdempotencyKeyJaProcessada()
    {
        // Arrange
        var transactionId = Guid.NewGuid();
        var accountId     = Guid.NewGuid();
        var command       = CriarCommand(accountId: accountId, amount: 300m);

        var respostaOriginal = new LedgerOperationResult(
            TransactionId: transactionId,
            AccountId:     accountId,
            Amount:        300m,
            BalanceAfter:  1300m,
            EntryType:     "Credit",
            ProcessedAt:   DateTimeOffset.UtcNow.AddMinutes(-10),
            CorrelationId: command.CorrelationId);

        var idempotencyRecord = IdempotencyRecord.Criar(
            command.IdempotencyKey,
            transactionId,
            System.Text.Json.JsonSerializer.Serialize(respostaOriginal));

        _idempotencyMock
            .Setup(r => r.ObterPorChaveAsync(command.IdempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(idempotencyRecord);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.TransactionId.Should().Be(transactionId);
        result.Amount.Should().Be(300m);
        result.BalanceAfter.Should().Be(1300m);
        result.IsIdempotentReplay.Should().BeTrue("deve sinalizar que é um replay");

        // Nenhuma operação de banco deve ter sido executada
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
        _accountRepoMock.Verify(r => r.ObterComLockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _entryRepoMock.Verify(r => r.InserirAsync(It.IsAny<LedgerEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisherMock.Verify(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ════════════════════════════════════════════════════════════════════════
    // CENÁRIO 3: Conta não encontrada
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Handle_DeveLancarKeyNotFoundException_QuandoContaNaoExiste()
    {
        // Arrange
        var command = CriarCommand();

        _idempotencyMock
            .Setup(r => r.ObterPorChaveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        _accountRepoMock
            .Setup(r => r.ObterComLockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((LedgerAccount?)null); // Conta não existe

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<KeyNotFoundException>()
            .WithMessage("*não encontrada*");

        // Rollback deve ter sido chamado
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    // ════════════════════════════════════════════════════════════════════════
    // CENÁRIO 4: Rollback em caso de falha no INSERT
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Handle_DeveExecutarRollback_QuandoInserirEntryFalha()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var account   = new LedgerAccountBuilder().ComId(accountId).Build();
        var command   = CriarCommand(accountId: accountId);

        _idempotencyMock
            .Setup(r => r.ObterPorChaveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        _accountRepoMock
            .Setup(r => r.ObterComLockAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        // Simula falha no banco durante INSERT
        _entryRepoMock
            .Setup(r => r.InserirAsync(It.IsAny<LedgerEntry>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Falha simulada no banco"));

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InvalidOperationException>();

        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once,
            "rollback deve ser chamado quando qualquer operação falha");
        _unitOfWorkMock.Verify(u => u.CommitAsync(It.IsAny<CancellationToken>()), Times.Never,
            "commit não deve ser chamado após falha");
        _publisherMock.Verify(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()), Times.Never,
            "eventos não devem ser publicados se o banco falhou");
    }

    // ════════════════════════════════════════════════════════════════════════
    // CENÁRIO 5: Evento de domínio publicado após commit
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Handle_DevePublicarDomainEvent_AposCommitBemSucedido()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var account   = new LedgerAccountBuilder().ComId(accountId).Build();
        var command   = CriarCommand(accountId: accountId, amount: 200m);

        _idempotencyMock
            .Setup(r => r.ObterPorChaveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        _accountRepoMock
            .Setup(r => r.ObterComLockAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — exatamente 1 evento publicado (PointsCreditedEvent)
        _publisherMock.Verify(
            p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()),
            Times.Once,
            "deve publicar exatamente 1 domain event após o commit");
    }
}
