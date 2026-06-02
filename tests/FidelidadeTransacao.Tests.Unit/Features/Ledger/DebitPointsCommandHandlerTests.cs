using FidelidadeTransacao.Application.Features.Ledger.Commands;
using FidelidadeTransacao.Application.Features.Ledger.Commands.DebitPoints;
using FidelidadeTransacao.Domain.Entities;
using FidelidadeTransacao.Domain.Exceptions;
using FidelidadeTransacao.Domain.Interfaces;
using FidelidadeTransacao.Tests.Unit.Helpers;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FidelidadeTransacao.Tests.Unit.Features.Ledger;

public sealed class DebitPointsCommandHandlerTests
{
    private readonly Mock<ILedgerAccountRepository> _accountRepoMock = new();
    private readonly Mock<ILedgerEntryRepository>   _entryRepoMock   = new();
    private readonly Mock<IIdempotencyRepository>   _idempotencyMock = new();
    private readonly Mock<IUnitOfWork>              _unitOfWorkMock  = new();
    private readonly Mock<IPublisher>               _publisherMock   = new();

    private readonly DebitPointsCommandHandler _handler;

    public DebitPointsCommandHandlerTests()
    {
        _handler = new DebitPointsCommandHandler(
            _accountRepoMock.Object,
            _entryRepoMock.Object,
            _idempotencyMock.Object,
            _unitOfWorkMock.Object,
            _publisherMock.Object,
            NullLogger<DebitPointsCommandHandler>.Instance);
    }

    private static DebitPointsCommand CriarCommand(
        Guid? accountId = null,
        decimal amount  = 300m) => new(
            AccountId:      accountId ?? Guid.NewGuid(),
            Amount:         amount,
            OccurrenceDate: DateTimeOffset.UtcNow,
            ReferenceId:    $"ORDER-{Guid.NewGuid()}",
            IdempotencyKey: $"key-{Guid.NewGuid()}",
            CorrelationId:  $"corr-{Guid.NewGuid()}",
            PartnerId:      "LIVELO",
            ProductName:    "Passagem Aérea",
            Description:    "TAM LA123");

    // ════════════════════════════════════════════════════════════════════════
    // CENÁRIO 1: Débito bem-sucedido
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Handle_DeveDebitarPontos_QuandoSaldoSuficiente()
    {
        // Arrange
        var accountId    = Guid.NewGuid();
        var saldoInicial = 1000m;
        var valorDebito  = 300m;

        var account = new LedgerAccountBuilder()
            .ComId(accountId)
            .ComSaldo(saldoInicial)
            .Build();

        var command = CriarCommand(accountId: accountId, amount: valorDebito);

        _idempotencyMock
            .Setup(r => r.ObterPorChaveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        _accountRepoMock
            .Setup(r => r.ObterComLockAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Amount.Should().Be(valorDebito);
        result.BalanceAfter.Should().Be(saldoInicial - valorDebito); // 700
        result.EntryType.Should().Be("Debit");
        result.IsIdempotentReplay.Should().BeFalse();
    }

    // ════════════════════════════════════════════════════════════════════════
    // CENÁRIO 2: RN01 — Saldo Insuficiente
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Handle_DeveLancarInsufficientBalanceException_QuandoSaldoInsuficiente()
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var account   = new LedgerAccountBuilder()
            .ComId(accountId)
            .ComSaldo(100m) // Saldo baixo
            .Build();

        var command = CriarCommand(accountId: accountId, amount: 500m); // Tenta debitar mais do que tem

        _idempotencyMock
            .Setup(r => r.ObterPorChaveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        _accountRepoMock
            .Setup(r => r.ObterComLockAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<InsufficientBalanceException>()
            .WithMessage("*Saldo insuficiente*");

        // Nenhum lançamento deve ter sido inserido
        _entryRepoMock.Verify(
            r => r.InserirAsync(It.IsAny<LedgerEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);

        // Rollback deve ter sido chamado
        _unitOfWorkMock.Verify(u => u.RollbackAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    // ════════════════════════════════════════════════════════════════════════
    // CENÁRIO 3: RN01 — Saldo exato (boundary test)
    // ════════════════════════════════════════════════════════════════════════
    [Theory]
    [InlineData(1000, 1000)]  // Saldo exato — deve passar
    [InlineData(1000, 999)]   // Saldo maior — deve passar
    [InlineData(1000, 1001)]  // Saldo insuficiente — deve falhar
    public async Task Handle_ValidaSaldoLimite_CorretamenteParaCadaCenario(
        decimal saldo, decimal valorDebito)
    {
        // Arrange
        var accountId = Guid.NewGuid();
        var account   = new LedgerAccountBuilder().ComId(accountId).ComSaldo(saldo).Build();
        var command   = CriarCommand(accountId: accountId, amount: valorDebito);

        _idempotencyMock
            .Setup(r => r.ObterPorChaveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        _accountRepoMock
            .Setup(r => r.ObterComLockAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        if (valorDebito <= saldo)
            await act.Should().NotThrowAsync();
        else
            await act.Should().ThrowAsync<InsufficientBalanceException>();
    }

    // ════════════════════════════════════════════════════════════════════════
    // CENÁRIO 4: RN03 — Idempotência no débito
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Handle_DeveRetornarRespostaOriginal_QuandoDebitoJaProcessado()
    {
        // Arrange
        var transactionId = Guid.NewGuid();
        var accountId     = Guid.NewGuid();
        var command       = CriarCommand(accountId: accountId, amount: 300m);

        var respostaOriginal = new LedgerOperationResult(
            TransactionId: transactionId,
            AccountId:     accountId,
            Amount:        300m,
            BalanceAfter:  700m,
            EntryType:     "Debit",
            ProcessedAt:   DateTimeOffset.UtcNow.AddMinutes(-5),
            CorrelationId: command.CorrelationId);

        _idempotencyMock
            .Setup(r => r.ObterPorChaveAsync(command.IdempotencyKey, It.IsAny<CancellationToken>()))
            .ReturnsAsync(IdempotencyRecord.Criar(
                command.IdempotencyKey,
                transactionId,
                System.Text.Json.JsonSerializer.Serialize(respostaOriginal)));

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.TransactionId.Should().Be(transactionId);
        result.BalanceAfter.Should().Be(700m);
        result.IsIdempotentReplay.Should().BeTrue();

        // Nenhum lock, nenhum INSERT, nenhum evento
        _accountRepoMock.Verify(r => r.ObterComLockAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
        _entryRepoMock.Verify(r => r.InserirAsync(It.IsAny<LedgerEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        _publisherMock.Verify(p => p.Publish(It.IsAny<INotification>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
