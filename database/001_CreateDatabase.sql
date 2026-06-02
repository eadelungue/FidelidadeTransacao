-- ============================================================================
-- PASSO 1: Criar o banco de dados
-- Execute este script conectado ao master (ou qualquer banco do servidor)
-- ============================================================================

USE master;
GO

IF NOT EXISTS (SELECT name FROM sys.databases WHERE name = 'LedgerDb')
BEGIN
    CREATE DATABASE LedgerDb
        COLLATE Latin1_General_100_CI_AS_SC_UTF8;
    PRINT 'Banco LedgerDb criado com sucesso.';
END
ELSE
BEGIN
    PRINT 'Banco LedgerDb já existe — pulando criação.';
END
GO
