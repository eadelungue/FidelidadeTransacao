# ============================================================================
# Setup Local — Ledger API
# Executa todos os passos para rodar o projeto localmente
#
# Pré-requisitos:
#   - .NET 8 SDK  (https://dotnet.microsoft.com/download)
#   - SQL Server  (LocalDB, Express, Developer ou Docker)
#
# Uso:
#   .\scripts\setup-local.ps1
#   .\scripts\setup-local.ps1 -SqlServer ".\SQLEXPRESS"
#   .\scripts\setup-local.ps1 -SqlServer "localhost,1433" -SqlUser "sa" -SqlPassword "SuaSenha"
# ============================================================================

param(
    [string]$SqlServer   = "localhost",
    [string]$SqlUser     = "",          # Vazio = Windows Auth (Trusted_Connection)
    [string]$SqlPassword = "",
    [switch]$SkipDb,                    # Pula criação do banco (se já existir)
    [switch]$SkipSeed                   # Pula inserção de dados de seed
)

$ErrorActionPreference = "Stop"
$Root = Split-Path $PSScriptRoot -Parent

# ── Cores ────────────────────────────────────────────────────────────────────
function Write-Step  { param($msg) Write-Host "`n▶ $msg" -ForegroundColor Cyan }
function Write-Ok    { param($msg) Write-Host "  ✓ $msg" -ForegroundColor Green }
function Write-Warn  { param($msg) Write-Host "  ⚠ $msg" -ForegroundColor Yellow }
function Write-Fail  { param($msg) Write-Host "  ✗ $msg" -ForegroundColor Red }

# ── Detecta dotnet ───────────────────────────────────────────────────────────
Write-Step "Verificando .NET SDK"

$dotnet = $null
foreach ($candidate in @("dotnet", "$env:USERPROFILE\.dotnet\dotnet.exe", "C:\Program Files\dotnet\dotnet.exe")) {
    try {
        $ver = & $candidate --version 2>$null
        if ($ver) { $dotnet = $candidate; break }
    } catch {}
}

if (-not $dotnet) {
    Write-Fail ".NET SDK não encontrado. Instale em: https://dotnet.microsoft.com/download"
    exit 1
}

$sdkVersion = & $dotnet --version
Write-Ok ".NET SDK $sdkVersion encontrado ($dotnet)"

# ── Detecta sqlcmd ───────────────────────────────────────────────────────────
Write-Step "Verificando sqlcmd"

$sqlcmd = $null
foreach ($candidate in @(
    "sqlcmd",
    "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\170\Tools\Binn\sqlcmd.exe",
    "C:\Program Files\Microsoft SQL Server\Client SDK\ODBC\130\Tools\Binn\sqlcmd.exe"
)) {
    try {
        $null = & $candidate -? 2>$null
        $sqlcmd = $candidate; break
    } catch {}
}

if (-not $sqlcmd) {
    Write-Warn "sqlcmd não encontrado. Execute os scripts SQL manualmente."
    Write-Warn "Download: https://aka.ms/sqlcmdinstall"
    $SkipDb   = $true
    $SkipSeed = $true
} else {
    Write-Ok "sqlcmd encontrado"
}

# ── Monta string de conexão para sqlcmd ──────────────────────────────────────
$sqlArgs = @("-S", $SqlServer, "-b")   # -b: aborta em erro
if ($SqlUser) {
    $sqlArgs += @("-U", $SqlUser, "-P", $SqlPassword)
} else {
    $sqlArgs += "-E"   # Windows Authentication
}

# ── Banco de dados ───────────────────────────────────────────────────────────
if (-not $SkipDb) {
    Write-Step "Criando banco de dados LedgerDb"
    try {
        & $sqlcmd @sqlArgs -i "$Root\database\001_CreateDatabase.sql"
        Write-Ok "Banco criado (ou já existia)"
    } catch {
        Write-Fail "Erro ao criar banco: $_"
        Write-Warn "Verifique se o SQL Server está rodando e as credenciais estão corretas."
        exit 1
    }

    Write-Step "Criando schema (tabelas, índices, trigger)"
    try {
        & $sqlcmd @sqlArgs -d "LedgerDb" -i "$Root\database\002_CreateSchema.sql"
        Write-Ok "Schema criado"
    } catch {
        Write-Fail "Erro ao criar schema: $_"
        exit 1
    }
}

if (-not $SkipSeed) {
    Write-Step "Inserindo dados de seed"
    try {
        & $sqlcmd @sqlArgs -d "LedgerDb" -i "$Root\database\003_SeedData.sql"
        Write-Ok "Seed inserido"
    } catch {
        Write-Fail "Erro ao inserir seed: $_"
        exit 1
    }
}

# ── Atualiza connection string no appsettings.Development.json ───────────────
Write-Step "Verificando appsettings.Development.json"

$appSettingsPath = "$Root\src\FidelidadeTransacao.API\appsettings.Development.json"
$appSettings     = Get-Content $appSettingsPath -Raw | ConvertFrom-Json

$currentCs = $appSettings.ConnectionStrings.DefaultConnection
Write-Ok "Connection string atual: $currentCs"

if ($SqlServer -ne "localhost" -or $SqlUser) {
    $newCs = if ($SqlUser) {
        "Server=$SqlServer;Database=LedgerDb;User Id=$SqlUser;Password=$SqlPassword;TrustServerCertificate=True;"
    } else {
        "Server=$SqlServer;Database=LedgerDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=True;"
    }

    $appSettings.ConnectionStrings.DefaultConnection = $newCs
    $appSettings | ConvertTo-Json -Depth 10 | Set-Content $appSettingsPath -Encoding UTF8
    Write-Ok "Connection string atualizada para: $newCs"
}

# ── Build ────────────────────────────────────────────────────────────────────
Write-Step "Build da solution"
& $dotnet build "$Root\FidelidadeTransacao.sln" --configuration Debug -v minimal
if ($LASTEXITCODE -ne 0) { Write-Fail "Build falhou"; exit 1 }
Write-Ok "Build concluído"

# ── Gera token JWT ───────────────────────────────────────────────────────────
Write-Step "Gerando token JWT para desenvolvimento"
& $dotnet run --project "$Root\tools\GenerateDevToken\GenerateDevToken.csproj" -- LedgerAdmin
Write-Ok "Token gerado acima — copie para usar no Swagger"

# ── Instruções finais ─────────────────────────────────────────────────────────
Write-Host ""
Write-Host "════════════════════════════════════════════════════════" -ForegroundColor Magenta
Write-Host "  Setup concluído! Para iniciar a API:" -ForegroundColor Magenta
Write-Host "" 
Write-Host "  cd src\FidelidadeTransacao.API" -ForegroundColor White
Write-Host "  dotnet run" -ForegroundColor White
Write-Host ""
Write-Host "  Swagger: https://localhost:7000/swagger" -ForegroundColor White
Write-Host "  HTTP:    http://localhost:5000/swagger" -ForegroundColor White
Write-Host ""
Write-Host "  Para gerar novo token:" -ForegroundColor White
Write-Host "  dotnet run --project tools\GenerateDevToken -- LedgerAdmin" -ForegroundColor White
Write-Host "════════════════════════════════════════════════════════" -ForegroundColor Magenta
