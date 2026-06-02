# Ledger API — Motor Transacional de Fidelidade

## Árvore de Diretórios

```
FidelidadeTransacao/
├── FidelidadeTransacao.sln
├── database/
│   └── 001_CreateSchema.sql          # DDL + trigger de imutabilidade (RN05)
│
├── src/
│   ├── FidelidadeTransacao.Domain/
│   │   ├── Common/
│   │   │   ├── BaseEntity.cs         # Domain Events
│   │   │   └── AuditableEntity.cs    # Auditoria somente-criação
│   │   ├── Entities/
│   │   │   ├── LedgerAccount.cs      # Conta com validação RN01
│   │   │   ├── LedgerEntry.cs        # Lançamento imutável (RN05)
│   │   │   └── IdempotencyRecord.cs  # Registro de idempotência (RN03)
│   │   ├── Enums/
│   │   │   ├── EntryType.cs          # Credit | Debit | Refund
│   │   │   └── AccountStatus.cs
│   │   ├── Events/
│   │   │   ├── PointsCreditedEvent.cs
│   │   │   ├── PointsDebitedEvent.cs
│   │   │   └── PointsRefundedEvent.cs
│   │   ├── Exceptions/
│   │   │   ├── DomainException.cs
│   │   │   ├── InsufficientBalanceException.cs  # RN01
│   │   │   └── IdempotencyConflictException.cs  # RN03
│   │   ├── Interfaces/
│   │   │   ├── ILedgerAccountRepository.cs  # ObterComLockAsync (RN02)
│   │   │   ├── ILedgerEntryRepository.cs    # Append-only (RN05)
│   │   │   ├── IIdempotencyRepository.cs    # RN03
│   │   │   └── IUnitOfWork.cs               # Controle transacional
│   │   └── ValueObjects/
│   │       └── StatementMetadata.cs
│   │
│   ├── FidelidadeTransacao.Application/
│   │   ├── Common/
│   │   │   ├── Behaviors/
│   │   │   │   ├── ValidationBehavior.cs    # Pipeline FluentValidation
│   │   │   │   └── LoggingBehavior.cs       # Pipeline de logs
│   │   │   ├── Exceptions/
│   │   │   │   └── ValidationException.cs
│   │   │   └── Interfaces/
│   │   │       └── IEventPublisher.cs       # Abstração Service Bus
│   │   ├── Features/
│   │   │   └── Ledger/
│   │   │       ├── Commands/
│   │   │       │   ├── LedgerOperationResult.cs
│   │   │       │   ├── CreditPoints/
│   │   │       │   │   ├── CreditPointsCommand.cs
│   │   │       │   │   ├── CreditPointsCommandValidator.cs
│   │   │       │   │   └── CreditPointsCommandHandler.cs  ← FLUXO COMPLETO
│   │   │       │   ├── DebitPoints/
│   │   │       │   │   ├── DebitPointsCommand.cs
│   │   │       │   │   ├── DebitPointsCommandValidator.cs
│   │   │       │   │   └── DebitPointsCommandHandler.cs
│   │   │       │   └── RefundPoints/
│   │   │       │       ├── RefundPointsCommand.cs
│   │   │       │       ├── RefundPointsCommandValidator.cs
│   │   │       │       └── RefundPointsCommandHandler.cs
│   │   │       └── EventHandlers/
│   │   │           ├── PointsCreditedEventHandler.cs  → Service Bus
│   │   │           ├── PointsDebitedEventHandler.cs   → Service Bus
│   │   │           └── PointsRefundedEventHandler.cs  → Service Bus
│   │   └── DependencyInjection.cs
│   │
│   ├── FidelidadeTransacao.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── DbConnectionFactory.cs       # IDbConnectionFactory (Dapper)
│   │   │   ├── UnitOfWork.cs                # IDbTransaction explícito
│   │   │   └── Repositories/
│   │   │       ├── LedgerAccountRepository.cs  # UPDLOCK + ROWLOCK (RN02)
│   │   │       ├── LedgerEntryRepository.cs    # INSERT-only (RN05)
│   │   │       └── IdempotencyRepository.cs
│   │   ├── Messaging/
│   │   │   └── ServiceBusEventPublisher.cs  # Azure Service Bus
│   │   └── DependencyInjection.cs
│   │
│   └── FidelidadeTransacao.API/
│       ├── Controllers/
│       │   └── LedgerController.cs          # /credit, /debit, /refund
│       ├── Middleware/
│       │   └── GlobalExceptionHandlerMiddleware.cs
│       ├── Program.cs                       # JWT + RateLimit + OTel + Swagger
│       └── appsettings.json
│
└── tests/
    └── FidelidadeTransacao.Tests.Unit/
        ├── Helpers/
        │   └── LedgerAccountBuilder.cs
        └── Features/Ledger/
            ├── CreditPointsCommandHandlerTests.cs  # 5 cenários
            ├── DebitPointsCommandHandlerTests.cs   # 4 cenários
            └── RefundPointsCommandHandlerTests.cs  # 5 cenários
```

## Regras de Negócio Implementadas

| RN | Descrição | Implementação |
|----|-----------|---------------|
| RN01 | Saldo Intransponível | `LedgerAccount.ValidarParaDebito()` + `CHECK CONSTRAINT` no SQL |
| RN02 | Lock Pessimista | `SELECT ... WITH (UPDLOCK, ROWLOCK)` em `ObterComLockAsync` |
| RN03 | Idempotência 48h | `IdempotencyRecord` persistido na mesma transação SQL |
| RN04 | Correlation ID | Header `X-Correlation-Id` propagado em todo o fluxo |
| RN05 | Histórico Imutável | Repositório append-only + `TRIGGER` no banco |

## Fluxo de uma Operação de Débito

```
POST /api/v1/ledger/debit
  → JWT Validation
  → Rate Limiter (50 req/min)
  → GlobalExceptionHandler
  → ValidationBehavior (FluentValidation)
  → LoggingBehavior
  → DebitPointsCommandHandler
      1. Verifica IdempotencyKey (sem lock)
      2. BEGIN TRANSACTION
      3. SELECT ... WITH (UPDLOCK, ROWLOCK)  ← RN02
      4. ValidarParaDebito()                 ← RN01
      5. INSERT LedgerEntries                ← RN05 (append-only)
      6. UPDATE LedgerAccounts SET Balance
      7. INSERT IdempotencyRecords           ← RN03
      8. COMMIT
      9. Publish PointsDebitedEvent          ← Service Bus (após commit)
  → HTTP 200 LedgerOperationResult
```
