# ============================================================================
# azure-bootstrap.ps1 — Provisionamento inicial da infraestrutura Azure
#
# Executa UMA VEZ para criar os resource groups e fazer o deploy do Bicep.
# Depois disso, o pipeline CI/CD cuida dos deploys de aplicação.
#
# Pré-requisitos:
#   - Azure CLI instalado e logado (az login)
#   - Bicep CLI (az bicep install)
#
# Uso:
#   .\scripts\azure-bootstrap.ps1 -Environment staging
#   .\scripts\azure-bootstrap.ps1 -Environment prod -SqlAdminPassword "SuaSenha"
# ============================================================================

param(
    [ValidateSet('staging', 'prod')]
    [string]$Environment = 'staging',

    [string]$Location = 'brazilsouth',
    [string]$Prefix   = 'fidelidade',

    [string]$SqlAdminLogin    = 'ledgeradmin',
    [string]$SqlAdminPassword = '',
    [string]$JwtSecretKey     = ''
)

$ErrorActionPreference = 'Stop'
$Root = Split-Path $PSScriptRoot -Parent

function Write-Step { param($msg) Write-Host "`n▶ $msg" -ForegroundColor Cyan }
function Write-Ok   { param($msg) Write-Host "  ✓ $msg" -ForegroundColor Green }
function Write-Fail { param($msg) Write-Host "  ✗ $msg" -ForegroundColor Red }

# ── Valida inputs sensíveis ───────────────────────────────────────────────────
if (-not $SqlAdminPassword) {
    $SqlAdminPassword = Read-Host "Senha do SQL Admin" -AsSecureString |
        [System.Runtime.InteropServices.Marshal]::PtrToStringAuto(
            [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($_))
}

if (-not $JwtSecretKey) {
    $JwtSecretKey = -join ((65..90) + (97..122) + (48..57) | Get-Random -Count 48 | ForEach-Object { [char]$_ })
    Write-Host "  JWT Secret gerado automaticamente (salve-o com segurança)" -ForegroundColor Yellow
}

# ── Verifica Azure CLI ────────────────────────────────────────────────────────
Write-Step "Verificando Azure CLI"
$null = az account show 2>&1
if ($LASTEXITCODE -ne 0) {
    Write-Fail "Não autenticado. Execute: az login"
    exit 1
}
$account = az account show | ConvertFrom-Json
Write-Ok "Subscription: $($account.name) ($($account.id))"

# ── Instala extensão Bicep se necessário ─────────────────────────────────────
Write-Step "Verificando Bicep"
az bicep install 2>&1 | Out-Null
Write-Ok "Bicep OK"

# ── Resource Group ────────────────────────────────────────────────────────────
$rgName = "rg-$Prefix-$Environment"
Write-Step "Criando Resource Group: $rgName"
az group create --name $rgName --location $Location | Out-Null
Write-Ok "Resource Group: $rgName ($Location)"

# ── Deploy Bicep ──────────────────────────────────────────────────────────────
Write-Step "Deploy da infraestrutura Bicep"

$deployParams = @(
    "--resource-group", $rgName,
    "--template-file",  "$Root\infra\main.bicep",
    "--parameters",
        "environment=$Environment",
        "location=$Location",
        "prefix=$Prefix",
        "sqlAdminLogin=$SqlAdminLogin",
        "sqlAdminPassword=$SqlAdminPassword",
        "jwtSecretKey=$JwtSecretKey"
)

$output = az deployment group create @deployParams --query properties.outputs | ConvertFrom-Json

if ($LASTEXITCODE -ne 0) {
    Write-Fail "Deploy Bicep falhou"
    exit 1
}

Write-Ok "Deploy concluído"

# ── Outputs ───────────────────────────────────────────────────────────────────
$acrServer    = $output.acrLoginServer.value
$appFqdn      = $output.containerAppFqdn.value
$sqlFqdn      = $output.sqlServerFqdn.value
$kvUri        = $output.keyVaultUri.value

Write-Host ""
Write-Host "════════════════════════════════════════════════════════" -ForegroundColor Magenta
Write-Host "  Infraestrutura provisionada!" -ForegroundColor Magenta
Write-Host ""
Write-Host "  ACR:         $acrServer"          -ForegroundColor White
Write-Host "  API URL:     https://$appFqdn"     -ForegroundColor White
Write-Host "  SQL Server:  $sqlFqdn"             -ForegroundColor White
Write-Host "  Key Vault:   $kvUri"               -ForegroundColor White
Write-Host ""
Write-Host "  Próximos passos:" -ForegroundColor Yellow
Write-Host "  1. Execute os scripts SQL no banco:" -ForegroundColor White
Write-Host "     sqlcmd -S $sqlFqdn -U $SqlAdminLogin -P <senha> -i database\001_CreateDatabase.sql" -ForegroundColor Gray
Write-Host "     sqlcmd -S $sqlFqdn -U $SqlAdminLogin -P <senha> -d LedgerDb -i database\002_CreateSchema.sql" -ForegroundColor Gray
Write-Host ""
Write-Host "  2. Configure os GitHub Secrets conforme docs\DEPLOY.md" -ForegroundColor White
Write-Host "  3. Faça push para a branch '$Environment' para disparar o pipeline" -ForegroundColor White
Write-Host "════════════════════════════════════════════════════════" -ForegroundColor Magenta
