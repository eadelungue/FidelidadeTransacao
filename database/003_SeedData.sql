-- ============================================================================
-- PASSO 3: Dados de seed para desenvolvimento e testes manuais
-- Execute conectado ao banco LedgerDb
-- ============================================================================

USE LedgerDb;
GO

-- IDs fixos para facilitar testes no Swagger/Postman
-- Copie estes valores para usar nas requisições

DECLARE @CustomerId1  UNIQUEIDENTIFIER = '11111111-1111-1111-1111-111111111111';
DECLARE @CustomerId2  UNIQUEIDENTIFIER = '22222222-2222-2222-2222-222222222222';

DECLARE @AccountId1   UNIQUEIDENTIFIER = 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
DECLARE @AccountId2   UNIQUEIDENTIFIER = 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
DECLARE @AccountId3   UNIQUEIDENTIFIER = 'cccccccc-cccc-cccc-cccc-cccccccccccc';

-- ── Contas ───────────────────────────────────────────────────────────────────

IF NOT EXISTS (SELECT 1 FROM LedgerAccounts WHERE Id = @AccountId1)
BEGIN
    INSERT INTO LedgerAccounts (Id, CustomerId, Balance, Status, CriadoEm, CriadoPor)
    VALUES (
        @AccountId1,
        @CustomerId1,
        5000.0000,      -- Saldo inicial: 5.000 pontos
        'Active',
        SYSDATETIMEOFFSET(),
        'seed'
    );
    PRINT 'Conta 1 criada (Cliente 1 — saldo 5.000 pts).';
END

IF NOT EXISTS (SELECT 1 FROM LedgerAccounts WHERE Id = @AccountId2)
BEGIN
    INSERT INTO LedgerAccounts (Id, CustomerId, Balance, Status, CriadoEm, CriadoPor)
    VALUES (
        @AccountId2,
        @CustomerId1,   -- Mesmo cliente, segunda carteira ("bolso")
        1000.0000,      -- Saldo inicial: 1.000 pontos
        'Active',
        SYSDATETIMEOFFSET(),
        'seed'
    );
    PRINT 'Conta 2 criada (Cliente 1 — segunda carteira — saldo 1.000 pts).';
END

IF NOT EXISTS (SELECT 1 FROM LedgerAccounts WHERE Id = @AccountId3)
BEGIN
    INSERT INTO LedgerAccounts (Id, CustomerId, Balance, Status, CriadoEm, CriadoPor)
    VALUES (
        @AccountId3,
        @CustomerId2,
        0.0000,         -- Conta zerada para testar RN01
        'Active',
        SYSDATETIMEOFFSET(),
        'seed'
    );
    PRINT 'Conta 3 criada (Cliente 2 — saldo zero, para testar RN01).';
END

-- ── Lançamentos históricos (para testar extrato e estorno) ───────────────────

DECLARE @Entry1 UNIQUEIDENTIFIER = 'e1111111-1111-1111-1111-111111111111';
DECLARE @Entry2 UNIQUEIDENTIFIER = 'e2222222-2222-2222-2222-222222222222';
DECLARE @Entry3 UNIQUEIDENTIFIER = 'e3333333-3333-3333-3333-333333333333';

IF NOT EXISTS (SELECT 1 FROM LedgerEntries WHERE Id = @Entry1)
BEGIN
    INSERT INTO LedgerEntries (
        Id, AccountId, Type, Amount, OccurrenceDate,
        IdempotencyKey, CorrelationId,
        PartnerId, ProductName, Description,
        CriadoEm, CriadoPor
    ) VALUES (
        @Entry1,
        @AccountId1,
        'Credit',
        5000.0000,
        SYSDATETIMEOFFSET(),
        'seed-credit-001',
        'seed-corr-001',
        'MAGALU',
        'Compra iPhone 15 Pro',
        'Pontos acumulados na compra #12345',
        SYSDATETIMEOFFSET(),
        'seed'
    );
    PRINT 'Lançamento de crédito seed criado (Entry1).';
END

IF NOT EXISTS (SELECT 1 FROM LedgerEntries WHERE Id = @Entry2)
BEGIN
    INSERT INTO LedgerEntries (
        Id, AccountId, Type, Amount, OccurrenceDate,
        IdempotencyKey, CorrelationId, ReferenceId,
        PartnerId, ProductName, Description,
        CriadoEm, CriadoPor
    ) VALUES (
        @Entry2,
        @AccountId2,
        'Credit',
        1000.0000,
        SYSDATETIMEOFFSET(),
        'seed-credit-002',
        'seed-corr-002',
        NULL,
        'LIVELO',
        'Bônus de boas-vindas',
        'Crédito promocional de cadastro',
        SYSDATETIMEOFFSET(),
        'seed'
    );
    PRINT 'Lançamento de crédito seed criado (Entry2).';
END

-- Lançamento de débito para testar estorno
IF NOT EXISTS (SELECT 1 FROM LedgerEntries WHERE Id = @Entry3)
BEGIN
    INSERT INTO LedgerEntries (
        Id, AccountId, Type, Amount, OccurrenceDate,
        IdempotencyKey, CorrelationId, ReferenceId,
        PartnerId, ProductName, Description,
        CriadoEm, CriadoPor
    ) VALUES (
        @Entry3,
        @AccountId1,
        'Debit',
        500.0000,
        SYSDATETIMEOFFSET(),
        'seed-debit-001',
        'seed-corr-003',
        'ORDER-SEED-001',
        'AMAZON',
        'Passagem Aérea GRU-GIG',
        'Resgate de milhas — voo TAM LA456',
        SYSDATETIMEOFFSET(),
        'seed'
    );
    PRINT 'Lançamento de débito seed criado (Entry3 — use para testar estorno).';
END

GO

-- ── Resumo para uso no Swagger ───────────────────────────────────────────────
PRINT '';
PRINT '=== IDs para usar no Swagger/Postman ===';
PRINT '';
PRINT 'AccountId com saldo 4.500 pts (5000 - 500 debit):';
PRINT '  aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa';
PRINT '';
PRINT 'AccountId com saldo 1.000 pts:';
PRINT '  bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb';
PRINT '';
PRINT 'AccountId com saldo ZERO (para testar RN01 - saldo insuficiente):';
PRINT '  cccccccc-cccc-cccc-cccc-cccccccccccc';
PRINT '';
PRINT 'OriginalTransactionId para testar Refund (débito de 500 pts):';
PRINT '  e3333333-3333-3333-3333-333333333333';
PRINT '';
PRINT '=== Seed concluído ===';
GO
