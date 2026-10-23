<#
.SYNOPSIS
    Imports the IBM TabFormer credit card dataset (~24M transactions, Apache-2.0, synthetic) into the
    demo tenants of a local Nexora database.

.DESCRIPTION
    1. Downloads and extracts the dataset into .data/tabformer (skipped when the CSV already exists).
    2. COPYs the CSV into an UNLOGGED staging table.
    3. Maps it onto tenants, branches and transactions with a single INSERT ... SELECT.
    4. Rebuilds the transaction indexes and runs VACUUM ANALYZE.
    Step 4 always runs, even if step 3 fails, so the table never stays without its indexes.

.EXAMPLE
    .\tools\tabformer\import-tabformer.ps1
    .\tools\tabformer\import-tabformer.ps1 -Reset          # remove a previous TabFormer import first
#>
param(
    [string]$Host_ = 'localhost',
    [int]$Port = 5432,
    [string]$Database = 'nexora',
    [string]$User = 'nexora',
    [string]$Password = 'nexora',
    [string]$Psql = 'C:\Program Files\PostgreSQL\18\bin\psql.exe',
    # Use another CSV with the same columns (e.g. a sample for a dry run).
    [string]$CsvPath,
    [switch]$Reset
)

$ErrorActionPreference = 'Stop'
$root = Resolve-Path (Join-Path $PSScriptRoot '..\..')
$dataDir = Join-Path $root '.data\tabformer'
$csv = if ($CsvPath) { (Resolve-Path $CsvPath).Path } else { Join-Path $dataDir 'card_transaction.v1.csv' }
$url = 'https://media.githubusercontent.com/media/IBM/TabFormer/main/data/credit_card/transactions.tgz'

$env:PGPASSWORD = $Password
$env:PGOPTIONS = '-c client_min_messages=warning'
$connection = @('-h', $Host_, '-p', $Port, '-U', $User, '-d', $Database, '-v', 'ON_ERROR_STOP=1', '-q')

# psql writes NOTICE/timing lines to stderr; Windows PowerShell 5.1 would turn those into
# terminating errors under 'Stop', so failures are detected by the exit code instead.
function Invoke-Psql([string[]]$Arguments) {
    $previous = $ErrorActionPreference
    $ErrorActionPreference = 'Continue'
    try { & $Psql @connection @Arguments 2>&1 | ForEach-Object { "$_" } }
    finally { $ErrorActionPreference = $previous }
    if ($LASTEXITCODE -ne 0) { throw "psql failed ($LASTEXITCODE): $Arguments" }
}

function Step([string]$Message) { Write-Host "`n[$(Get-Date -Format HH:mm:ss)] $Message" -ForegroundColor Cyan }

if (-not (Test-Path $csv)) {
    Step 'Downloading TabFormer (~266 MB)...'
    New-Item -ItemType Directory -Force $dataDir | Out-Null
    $ProgressPreference = 'SilentlyContinue'
    Invoke-WebRequest -Uri $url -OutFile (Join-Path $dataDir 'transactions.tgz') -UseBasicParsing
    Step 'Extracting (~2.2 GB)...'
    tar -xzf (Join-Path $dataDir 'transactions.tgz') -C $dataDir
}

if ($Reset) {
    Step 'Removing a previous TabFormer import...'
    Invoke-Psql @('-c', "DELETE FROM transactions WHERE reference LIKE 'TRX-%-TF%';",
                  '-c', "DELETE FROM branches b WHERE b.code LIKE 'TF-%' AND NOT EXISTS (SELECT 1 FROM transactions t WHERE t.branch_id = b.id) AND NOT EXISTS (SELECT 1 FROM users u WHERE u.branch_id = b.id);")
}

$started = Get-Date
Step 'Preparing staging table...'
Invoke-Psql @('-f', (Join-Path $PSScriptRoot '01-stage.sql'))

Step 'Loading CSV into staging (COPY)...'
$csvPath = $csv.Replace('\', '/').Replace("'", "''")
Invoke-Psql @('-c', "\copy tabformer_raw FROM '$csvPath' WITH (FORMAT csv, HEADER true)")

try {
    Step 'Mapping onto tenants, branches and transactions (this is the long step)...'
    Invoke-Psql @('-f', (Join-Path $PSScriptRoot '02-load.sql'))
}
finally {
    Step 'Rebuilding indexes...'
    Invoke-Psql @('-f', (Join-Path $PSScriptRoot '03-finish.sql'))
    Step 'VACUUM ANALYZE (statistics + visibility map for index-only scans)...'
    Invoke-Psql @('-c', 'VACUUM (ANALYZE) transactions;', '-c', 'VACUUM (ANALYZE) branches;')
}

Step 'Result:'
& $Psql @connection -P pager=off -c @"
SELECT t.slug AS tenant,
       count(*) AS transactions,
       count(*) FILTER (WHERE x.status = 'Pending') AS pending,
       count(*) FILTER (WHERE x.status = 'Rejected') AS rejected,
       min(x.created_at)::date AS first_day,
       max(x.created_at)::date AS last_day
FROM transactions x JOIN tenants t ON t.id = x.tenant_id
GROUP BY t.slug ORDER BY t.slug;
"@
& $Psql @connection -P pager=off -c "SELECT pg_size_pretty(pg_total_relation_size('transactions')) AS transactions_size, pg_size_pretty(pg_database_size(current_database())) AS database_size;"
Write-Host ("`nDone in {0:hh\:mm\:ss}." -f ((Get-Date) - $started)) -ForegroundColor Green
