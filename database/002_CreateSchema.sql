-- ============================================================================
-- PASSO 2: Criar tabelas, índices e trigger
-- Execute conectado ao banco LedgerDb
-- ============================================================================

USE LedgerDb;
GO

-- ── Idempotente: só cria se não existir ──────────────────────────────────────

-- ── 1. LedgerAccounts ────────────────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'LedgerAccounts')
BEGIN
    CREATE TABLE LedgerAccounts (
        Id          UNIQUEIDENTIFIER    NOT NULL,
        CustomerId  UNIQUEIDENTIFIER    NOT NULL,
        Balance     DECIMAL(18, 4)      NOT NULL CONSTRAINT DF_LedgerAccounts_Balance DEFAULT 0,
        Status      NVARCHAR(20)        NOT NULL CONSTRAINT DF_LedgerAccounts_Status  DEFAULT 'Active',
        CriadoEm   DATETIMEOFFSET      NOT NULL CONSTRAINT DF_LedgerAccounts_CriadoEm DEFAULT SYSDATETIMEOFFSET(),
        CriadoPor  NVARCHAR(100)       NOT NULL,

        CONSTRAINT PK_LedgerAccounts PRIMARY KEY CLUSTERED (Id),

        -- RN01: saldo nunca pode ser negativo — defesa em profundidade no banco
        CONSTRAINT CK_LedgerAccounts_Balance CHECK (Balance >= 0),
        CONSTRAINT CK_LedgerAccounts_Status  CHECK (Status IN ('Active', 'Blocked', 'Closed'))
    );

    CREATE NONCLUSTERED INDEX IX_LedgerAccounts_CustomerId
        ON LedgerAccounts (CustomerId);

    PRINT 'Tabela LedgerAccounts criada.';
END
ELSE
    PRINT 'Tabela LedgerAccounts já existe — pulando.';
GO

-- ── 2. LedgerEntries (append-only — RN05) ────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'LedgerEntries')
BEGIN
    CREATE TABLE LedgerEntries (
        Id              UNIQUEIDENTIFIER    NOT NULL,
        AccountId       UNIQUEIDENTIFIER    NOT NULL,
        Type            NVARCHAR(10)        NOT NULL,
        Amount          DECIMAL(18, 4)      NOT NULL,
        OccurrenceDate  DATETIMEOFFSET      NOT NULL,
        IdempotencyKey  NVARCHAR(256)       NOT NULL,
        CorrelationId   NVARCHAR(128)       NOT NULL,
        OriginalEntryId UNIQUEIDENTIFIER    NULL,
        ReferenceId     NVARCHAR(256)       NULL,
        RefundReason    NVARCHAR(500)       NULL,
        -- StatementMetadata desnormalizado (Value Object → colunas)
        PartnerId       NVARCHAR(50)        NOT NULL,
        ProductName     NVARCHAR(200)       NOT NULL,
        Description     NVARCHAR(500)       NULL,
        -- Auditoria somente-criação
        CriadoEm       DATETIMEOFFSET      NOT NULL CONSTRAINT DF_LedgerEntries_CriadoEm DEFAULT SYSDATETIMEOFFSET(),
        CriadoPor      NVARCHAR(100)       NOT NULL,

        CONSTRAINT PK_LedgerEntries PRIMARY KEY CLUSTERED (Id),

        CONSTRAINT FK_LedgerEntries_Account
            FOREIGN KEY (AccountId) REFERENCES LedgerAccounts (Id),

        CONSTRAINT FK_LedgerEntries_OriginalEntry
            FOREIGN KEY (OriginalEntryId) REFERENCES LedgerEntries (Id),

        CONSTRAINT CK_LedgerEntries_Amount
            CHECK (Amount > 0),

        CONSTRAINT CK_LedgerEntries_Type
            CHECK (Type IN ('Credit', 'Debit', 'Refund'))
    );

    -- Índice principal: busca de extrato por conta ordenado por data
    CREATE NONCLUSTERED INDEX IX_LedgerEntries_AccountId_CriadoEm
        ON LedgerEntries (AccountId, CriadoEm DESC)
        INCLUDE (Type, Amount, OccurrenceDate, PartnerId, ProductName);

    -- Índice para lookup de idempotência (RN03)
    CREATE UNIQUE NONCLUSTERED INDEX IX_LedgerEntries_IdempotencyKey
        ON LedgerEntries (IdempotencyKey);

    -- Índice para rastreabilidade (RN04)
    CREATE NONCLUSTERED INDEX IX_LedgerEntries_CorrelationId
        ON LedgerEntries (CorrelationId);

    -- Índice para busca de lançamento original em estornos
    CREATE NONCLUSTERED INDEX IX_LedgerEntries_OriginalEntryId
        ON LedgerEntries (OriginalEntryId)
        WHERE OriginalEntryId IS NOT NULL;

    PRINT 'Tabela LedgerEntries criada.';
END
ELSE
    PRINT 'Tabela LedgerEntries já existe — pulando.';
GO

-- ── 3. Trigger de Imutabilidade (RN05) ───────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.triggers WHERE name = 'TR_LedgerEntries_PreventModification')
BEGIN
    EXEC('
    CREATE TRIGGER TR_LedgerEntries_PreventModification
    ON LedgerEntries
    AFTER UPDATE, DELETE
    AS
    BEGIN
        SET NOCOUNT ON;
        RAISERROR (
            ''Operação proibida: lançamentos do Ledger são imutáveis (RN05). '' +
            ''Use um novo lançamento de estorno para compensar.'',
            16, 1
        );
        ROLLBACK TRANSACTION;
    END;
    ');
    PRINT 'Trigger TR_LedgerEntries_PreventModification criado.';
END
ELSE
    PRINT 'Trigger já existe — pulando.';
GO

-- ── 4. IdempotencyRecords (RN03) ─────────────────────────────────────────────
IF NOT EXISTS (SELECT 1 FROM sys.tables WHERE name = 'IdempotencyRecords')
BEGIN
    CREATE TABLE IdempotencyRecords (
        IdempotencyKey  NVARCHAR(256)       NOT NULL,
        TransactionId   UNIQUEIDENTIFIER    NOT NULL,
        ResponsePayload NVARCHAR(MAX)       NOT NULL,
        CriadoEm       DATETIMEOFFSET      NOT NULL CONSTRAINT DF_IdempotencyRecords_CriadoEm DEFAULT SYSDATETIMEOFFSET(),
        ExpiresAt       DATETIMEOFFSET      NOT NULL,

        CONSTRAINT PK_IdempotencyRecords PRIMARY KEY CLUSTERED (IdempotencyKey)
    );

    -- Índice para job de limpeza de registros expirados
    CREATE NONCLUSTERED INDEX IX_IdempotencyRecords_ExpiresAt
        ON IdempotencyRecords (ExpiresAt);

    PRINT 'Tabela IdempotencyRecords criada.';
END
ELSE
    PRINT 'Tabela IdempotencyRecords já existe — pulando.';
GO

PRINT '=== Schema criado com sucesso em LedgerDb ===';
GO
