-- ============================================================================
-- Ledger Database Schema
-- Motor Transacional de Fidelidade
--
-- PRINCÍPIOS APLICADOS:
-- 1. Tabela LedgerEntries é append-only (trigger bloqueia UPDATE/DELETE — RN05)
-- 2. Índices otimizados para os padrões de acesso do Ledger
-- 3. Constraints garantem integridade mesmo sem o domínio (defesa em profundidade)
-- ============================================================================

-- ── Contas do Ledger ─────────────────────────────────────────────────────────
CREATE TABLE LedgerAccounts (
    Id          UNIQUEIDENTIFIER    NOT NULL,
    CustomerId  UNIQUEIDENTIFIER    NOT NULL,
    Balance     DECIMAL(18, 4)      NOT NULL DEFAULT 0,
    Status      NVARCHAR(20)        NOT NULL DEFAULT 'Active',
    CriadoEm   DATETIMEOFFSET      NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    CriadoPor  NVARCHAR(100)       NOT NULL,

    CONSTRAINT PK_LedgerAccounts PRIMARY KEY (Id),
    CONSTRAINT CK_LedgerAccounts_Balance CHECK (Balance >= 0),  -- RN01: saldo nunca negativo
    CONSTRAINT CK_LedgerAccounts_Status  CHECK (Status IN ('Active', 'Blocked', 'Closed'))
);

CREATE INDEX IX_LedgerAccounts_CustomerId ON LedgerAccounts (CustomerId);

-- ── Lançamentos do Ledger (append-only) ──────────────────────────────────────
CREATE TABLE LedgerEntries (
    Id              UNIQUEIDENTIFIER    NOT NULL,
    AccountId       UNIQUEIDENTIFIER    NOT NULL,
    Type            NVARCHAR(10)        NOT NULL,
    Amount          DECIMAL(18, 4)      NOT NULL,
    OccurrenceDate  DATETIMEOFFSET      NOT NULL,
    IdempotencyKey  NVARCHAR(256)       NOT NULL,
    CorrelationId   NVARCHAR(128)       NOT NULL,
    OriginalEntryId UNIQUEIDENTIFIER    NULL,       -- Preenchido apenas em Refunds
    ReferenceId     NVARCHAR(256)       NULL,       -- ID do pedido externo (Debits)
    RefundReason    NVARCHAR(500)       NULL,       -- Motivo do estorno (Refunds)
    -- Metadados de extrato (StatementMetadata desnormalizado)
    PartnerId       NVARCHAR(50)        NOT NULL,
    ProductName     NVARCHAR(200)       NOT NULL,
    Description     NVARCHAR(500)       NULL,
    -- Auditoria
    CriadoEm       DATETIMEOFFSET      NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    CriadoPor      NVARCHAR(100)       NOT NULL,

    CONSTRAINT PK_LedgerEntries PRIMARY KEY (Id),
    CONSTRAINT FK_LedgerEntries_Account FOREIGN KEY (AccountId)
        REFERENCES LedgerAccounts (Id),
    CONSTRAINT FK_LedgerEntries_OriginalEntry FOREIGN KEY (OriginalEntryId)
        REFERENCES LedgerEntries (Id),
    CONSTRAINT CK_LedgerEntries_Amount CHECK (Amount > 0),
    CONSTRAINT CK_LedgerEntries_Type   CHECK (Type IN ('Credit', 'Debit', 'Refund'))
);

-- Índices para os padrões de acesso mais frequentes
CREATE INDEX IX_LedgerEntries_AccountId      ON LedgerEntries (AccountId, CriadoEm DESC);
CREATE INDEX IX_LedgerEntries_IdempotencyKey ON LedgerEntries (IdempotencyKey);
CREATE INDEX IX_LedgerEntries_CorrelationId  ON LedgerEntries (CorrelationId);
CREATE INDEX IX_LedgerEntries_OriginalEntry  ON LedgerEntries (OriginalEntryId) WHERE OriginalEntryId IS NOT NULL;

-- ── TRIGGER: Proteção de Imutabilidade (RN05) ────────────────────────────────
-- Defesa em profundidade: bloqueia UPDATE e DELETE diretamente no banco,
-- independente de qual aplicação tente modificar os dados.
GO
CREATE TRIGGER TR_LedgerEntries_PreventModification
ON LedgerEntries
AFTER UPDATE, DELETE
AS
BEGIN
    RAISERROR (
        'Operação proibida: lançamentos do Ledger são imutáveis (RN05). ' +
        'Use um novo lançamento de estorno para compensar.',
        16, 1
    );
    ROLLBACK TRANSACTION;
END;
GO

-- ── Registros de Idempotência (RN03) ─────────────────────────────────────────
CREATE TABLE IdempotencyRecords (
    IdempotencyKey  NVARCHAR(256)       NOT NULL,
    TransactionId   UNIQUEIDENTIFIER    NOT NULL,
    ResponsePayload NVARCHAR(MAX)       NOT NULL,   -- JSON da resposta original
    CriadoEm       DATETIMEOFFSET      NOT NULL DEFAULT SYSDATETIMEOFFSET(),
    ExpiresAt       DATETIMEOFFSET      NOT NULL,   -- TTL de 48h

    CONSTRAINT PK_IdempotencyRecords PRIMARY KEY (IdempotencyKey)
);

CREATE INDEX IX_IdempotencyRecords_ExpiresAt ON IdempotencyRecords (ExpiresAt);

-- ── Job de limpeza de registros expirados ────────────────────────────────────
-- Executar via SQL Agent ou Azure Elastic Jobs periodicamente
-- DELETE FROM IdempotencyRecords WHERE ExpiresAt < SYSDATETIMEOFFSET();
