<#
.SYNOPSIS
    Copies local secrets from the repo-root .env into dotnet user-secrets for Erp.Api.

.DESCRIPTION
    User-secrets live in your user profile (%APPDATA%\Microsoft\UserSecrets\<id>\secrets.json),
    outside the repository, and are loaded automatically in the Development environment.
    In production the same keys come from environment variables or a vault, e.g.
      ConnectionStrings__ErpDb, Jwt__SigningKey  (double underscore = section separator)

.EXAMPLE
    ./Backend/scripts/set-dev-secrets.ps1
#>
param([string]$SqlServer = 'localhost,14333')

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$envFile = Join-Path $root '.env'
if (-not (Test-Path $envFile)) { throw "Missing $envFile. Copy .env.example to .env first." }

$values = @{}
Get-Content $envFile | Where-Object { $_ -match '^\s*([A-Z0-9_]+)=(.*)$' } | ForEach-Object {
    $values[$Matches[1]] = $Matches[2]
}
foreach ($key in 'SQL_APP_PASSWORD', 'JWT_SIGNING_KEY') {
    if (-not $values[$key]) { throw "$key is missing in .env" }
}

$api = Join-Path $root 'Backend\src\Erp\Erp.Api'
# The API connects with the least-privilege login created by 05_app_login.sql, never 'sa'
$connection = "Server=$SqlServer;Database=ErpDb;User Id=minierp_app;Password=$($values['SQL_APP_PASSWORD']);" +
              'TrustServerCertificate=True;Encrypt=True;Application Name=MiniErp.Api'

dotnet user-secrets set 'ConnectionStrings:ErpDb' $connection --project $api | Out-Null
dotnet user-secrets set 'Jwt:SigningKey' $values['JWT_SIGNING_KEY'] --project $api | Out-Null
Write-Host 'User-secrets set for Erp.Api: ConnectionStrings:ErpDb, Jwt:SigningKey' -ForegroundColor Green
