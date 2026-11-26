<#
.SYNOPSIS
    Creates ErpDb and PracticeDb and runs every SQL script in order.

.EXAMPLE
    ./Backend/database/run-all.ps1            # drop + recreate both databases, run everything
    ./Backend/database/run-all.ps1 -SkipPractice
    ./Backend/database/run-all.ps1 -File practice/03_nth_highest_salary.sql   # run one script

.NOTES
    - Reads SQL_SA_PASSWORD from the repo-root .env file (git-ignored).
    - The password is passed through the SQLCMDPASSWORD environment variable,
      so it never appears on the command line or in the process list.
    - sqlcmd flags: -b stop on error, -I QUOTED_IDENTIFIER ON (required for
      filtered indexes, indexed views, computed-column indexes),
      -f 65001 read files as UTF-8 (Hindi/Marathi text), -C trust the
      container's self-signed certificate.
    - Scripts whose first lines contain "@manual" (two-session demos) are skipped.
#>
param(
    [string]$Server = 'localhost,14333',
    [switch]$SkipPractice,
    [switch]$NoReset,
    [string]$File
)

$ErrorActionPreference = 'Stop'
$dbDir   = $PSScriptRoot
$envFile = Join-Path $PSScriptRoot '..\..\.env'

if (-not (Test-Path $envFile)) { throw "Missing $envFile. Copy .env.example to .env first." }
$pwLine = Get-Content $envFile | Where-Object { $_ -match '^SQL_SA_PASSWORD=' } | Select-Object -First 1
if (-not $pwLine) { throw 'SQL_SA_PASSWORD not found in .env' }
$env:SQLCMDPASSWORD = $pwLine.Substring('SQL_SA_PASSWORD='.Length)

function Invoke-SqlFile([string]$path) {
    Write-Host "==> $($path.Replace($dbDir, '').TrimStart('\', '/'))" -ForegroundColor Cyan
    & sqlcmd -S $Server -U sa -C -b -I -f 65001 -i $path
    if ($LASTEXITCODE -ne 0) { throw "Script failed: $path" }
}

function Invoke-SqlQuery([string]$query) {
    & sqlcmd -S $Server -U sa -C -b -I -Q $query
    if ($LASTEXITCODE -ne 0) { throw "Query failed: $query" }
}

try {
    if ($File) {
        Invoke-SqlFile (Join-Path $dbDir $File)
        return
    }

    if (-not $NoReset) {
        Write-Host '==> Dropping ErpDb and PracticeDb (if they exist)' -ForegroundColor Yellow
        foreach ($db in 'ErpDb', 'PracticeDb') {
            Invoke-SqlQuery "IF DB_ID('$db') IS NOT NULL BEGIN ALTER DATABASE [$db] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [$db]; END"
        }
    }

    # Main ErpDb scripts, in numeric order
    Get-ChildItem $dbDir -Filter '*.sql' | Sort-Object Name | ForEach-Object { Invoke-SqlFile $_.FullName }

    if (-not $SkipPractice) {
        Get-ChildItem (Join-Path $dbDir 'practice') -Filter '*.sql' | Sort-Object Name | ForEach-Object {
            $head = Get-Content $_.FullName -TotalCount 15 -Encoding utf8
            if ($head -match '@manual') {
                Write-Host "--  skipped (manual two-session demo): practice/$($_.Name)" -ForegroundColor DarkGray
            } else {
                Invoke-SqlFile $_.FullName
            }
        }
    }
    Write-Host 'All scripts completed.' -ForegroundColor Green
}
finally {
    Remove-Item Env:SQLCMDPASSWORD -ErrorAction SilentlyContinue
}
