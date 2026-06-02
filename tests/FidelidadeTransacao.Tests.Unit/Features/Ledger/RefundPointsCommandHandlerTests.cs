using FidelidadeTransacao.Application.Features.Ledger.Commands;
using FidelidadeTransacao.Application.Features.Ledger.Commands.RefundPoints;
using FidelidadeTransacao.Domain.Entities;
using FidelidadeTransacao.Domain.Enums;
using FidelidadeTransacao.Domain.Exceptions;
using FidelidadeTransacao.Domain.Interfaces;
using FidelidadeTransacao.Tests.Unit.Helpers;
using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace FidelidadeTransacao.Tests.Unit.Features.Ledger;

public sealed class RefundPointsCommandHandlerTests
{
    private readonly Mock<ILedgerAccountRepository> _accountRepoMock = new();
    private readonly Mock<ILedgerEntryRepository>   _entryRepoMock   = new();
    private readonly Mock<IIdempotencyRepository>   _idempotencyMock = new();
    private readonly Mock<IUnitOfWork>              _unitOfWorkMock  = new();
    private readonly Mock<IPublisher>               _publisherMock   = new();

    private readonly RefundPointsCommandHandler _handler;

    public RefundPointsCommandHandlerTests()
    {
        _handler = new RefundPointsCommandHandler(
            _accountRepoMock.Object,
            _entryRepoMock.Object,
            _idempotencyMock.Object,
            _unitOfWorkMock.Object,
            _publisherMock.Object,
            NullLogger<RefundPointsCommandHandler>.Instance);
    }

    /// <summary>
    /// Cria um LedgerEntry de Debit via reflection para simular o lançamento original.
    /// </summary>
    private static LedgerEntry CriarEntryDebit(Guid accountId, decimal amount)
    {
        var entry = (LedgerEntry)Activator.CreateInstance(typeof(LedgerEntry), nonPublic: true)!;
        Set(entry, "Id",          Guid.NewGuid());
        Set(entry, "AccountId",   accountId);
        Set(entry, "Type",        EntryType.Debit);
        Set(entry, "Amount",      amount);
        Set(entry, "CriadoEm",    DateTimeOffset.UtcNow.AddHours(-1));
        Set(entry, "CriadoPor",   "test");
        Set(entry, "IdempotencyKey", $"orig-{Guid.NewGuid()}");
        Set(entry, "CorrelationId",  $"corr-{Guid.NewGuid()}");
        Set(entry, "StatementMetadata",
            FidelidadeTransacao.Domain.ValueObjects.StatementMetadata.Create("MAGALU", "Produto", null));
        return entry;
    }

    private static void Set(object obj, string prop, object? value)
        => obj.GetType().GetProperty(prop)!.SetValue(obj, value);

    private RefundPointsCommand CriarCommand(
        Guid accountId,
        Guid originalEntryId,
        decimal amount = 200m) => new(
            AccountId:             accountId,
            OriginalTransactionId: originalEntryId,
            Amount:                amount,
            OccurrenceDate:        DateTimeOffset.UtcNow,
            Reason:                "Arrependimento de compra",
            IdempotencyKey:        $"refund-{Guid.NewGuid()}",
            CorrelationId:         $"corr-{Guid.NewGuid()}",
            PartnerId:             "MAGALU",
            ProductName:           "Estorno iPhone 15 Pro",
            Description:           null);

    // ════════════════════════════════════════════════════════════════════════
    // CENÁRIO 1: Estorno de Debit — devolve pontos (saldo aumenta)
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Handle_DeveRestaurarPontos_QuandoEstornoDeDebitValido()
    {
        // Arrange
        var accountId    = Guid.NewGuid();
        var saldoAtual   = 700m;
        var valorOriginal = 300m;
        var valorEstorno  = 300m; // Estorno total

        var account      = new LedgerAccountBuilder().ComId(accountId).ComSaldo(saldoAtual).Build();
        var originalEntry = CriarEntryDebit(accountId, valorOriginal);
        var command       = CriarCommand(accountId, originalEntry.Id, valorEstorno);

        _idempotencyMock
            .Setup(r => r.ObterPorChaveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        _accountRepoMock
            .Setup(r => r.ObterComLockAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        _entryRepoMock
            .Setup(r => r.ObterPorIdAsync(originalEntry.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(originalEntry);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.EntryType.Should().Be("Refund");
        result.Amount.Should().Be(valorEstorno);
        result.BalanceAfter.Should().Be(saldoAtual + valorEstorno); // 700 + 300 = 1000
        result.IsIdempotentReplay.Should().BeFalse();
    }

    // ════════════════════════════════════════════════════════════════════════
    // CENÁRIO 2: Estorno parcial
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Handle_DevePermitirEstornoParcial_QuandoAmountMenorQueOriginal()
    {
        // Arrange
        var accountId     = Guid.NewGuid();
        var account       = new LedgerAccountBuilder().ComId(accountId).ComSaldo(500m).Build();
        var originalEntry = CriarEntryDebit(accountId, 300m);
        var command       = CriarCommand(accountId, originalEntry.Id, amount: 100m); // Estorno parcial

        _idempotencyMock
            .Setup(r => r.ObterPorChaveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        _accountRepoMock
            .Setup(r => r.ObterComLockAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        _entryRepoMock
            .Setup(r => r.ObterPorIdAsync(originalEntry.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(originalEntry);

        // Act
        var result = await _handler.Handle(command, CancellationToken.None);

        // Assert
        result.Amount.Should().Be(100m);
        result.BalanceAfter.Should().Be(600m); // 500 + 100
    }

    // ════════════════════════════════════════════════════════════════════════
    // CENÁRIO 3: Estorno excede valor original — deve falhar
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Handle_DeveLancarDomainException_QuandoEstornoExcedeOriginal()
    {
        // Arrange
        var accountId     = Guid.NewGuid();
        var account       = new LedgerAccountBuilder().ComId(accountId).ComSaldo(500m).Build();
        var originalEntry = CriarEntryDebit(accountId, 200m); // Original: 200
        var command       = CriarCommand(accountId, originalEntry.Id, amount: 300m); // Tenta estornar 300

        _idempotencyMock
            .Setup(r => r.ObterPorChaveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        _accountRepoMock
            .Setup(r => r.ObterComLockAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        _entryRepoMock
            .Setup(r => r.ObterPorIdAsync(originalEntry.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(originalEntry);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*não pode exceder*");

        _entryRepoMock.Verify(
            r => r.InserirAsync(It.IsAny<LedgerEntry>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // ════════════════════════════════════════════════════════════════════════
    // CENÁRIO 4: Lançamento original não pertence à conta — deve falhar
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Handle_DeveLancarDomainException_QuandoEntryNaoPertenceAConta()
    {
        // Arrange
        var accountId      = Guid.NewGuid();
        var outraContaId   = Guid.NewGuid(); // Conta diferente
        var account        = new LedgerAccountBuilder().ComId(accountId).Build();
        var originalEntry  = CriarEntryDebit(outraContaId, 200m); // Entry de outra conta
        var command        = CriarCommand(accountId, originalEntry.Id);

        _idempotencyMock
            .Setup(r => r.ObterPorChaveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        _accountRepoMock
            .Setup(r => r.ObterComLockAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        _entryRepoMock
            .Setup(r => r.ObterPorIdAsync(originalEntry.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(originalEntry);

        // Act
        var act = () => _handler.Handle(command, CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<DomainException>()
            .WithMessage("*não pertence à conta*");
    }

    // ════════════════════════════════════════════════════════════════════════
    // CENÁRIO 5: RN05 — Lançamento original não é modificado
    // ════════════════════════════════════════════════════════════════════════
    [Fact]
    public async Task Handle_NaoDeveModificarLancamentoOriginal_AoEstornar()
    {
        // Arrange
        var accountId     = Guid.NewGuid();
        var account       = new LedgerAccountBuilder().ComId(accountId).ComSaldo(500m).Build();
        var originalEntry = CriarEntryDebit(accountId, 300m);
        var amountOriginal = originalEntry.Amount; // Captura antes
        var command       = CriarCommand(accountId, originalEntry.Id, amount: 300m);

        _idempotencyMock
            .Setup(r => r.ObterPorChaveAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((IdempotencyRecord?)null);

        _accountRepoMock
            .Setup(r => r.ObterComLockAsync(accountId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(account);

        _entryRepoMock
            .Setup(r => r.ObterPorIdAsync(originalEntry.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(originalEntry);

        // Act
        await _handler.Handle(command, CancellationToken.None);

        // Assert — o lançamento original não foi alterado (RN05)
        originalEntry.Amount.Should().Be(amountOriginal,
            "o lançamento original nunca deve ser modificado");
        originalEntry.Type.Should().Be(EntryType.Debit,
            "o tipo do lançamento original não deve mudar");

        // Verifica que não houve UPDATE no entry original — apenas INSERT do novo
        // (O repositório não tem método Update — verificamos que InserirAsync foi chamado 1x)
        _entryRepoMock.Verify(
            r => r.InserirAsync(
                It.Is<LedgerEntry>(e => e.Type == EntryType.Refund),
                It.IsAny<CancellationToken>()),
            Times.Once,
            "deve inserir apenas o novo lançamento de estorno");
    }
}
