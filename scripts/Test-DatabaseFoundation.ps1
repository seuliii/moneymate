[CmdletBinding()]
param([string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin')

# Integration verification against an isolated, password-protected PostgreSQL cluster.
# Never connects to or modifies the installed localhost:5432 server.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$testRoot = Join-Path $root ('artifacts\db-check-' + [Guid]::NewGuid().ToString('N'))
$null = New-Item -ItemType Directory -Path $testRoot -Force
$dataPath = Join-Path $testRoot 'data'
$passwordFile = Join-Path $testRoot 'test-password.txt'
$testPassword = [Guid]::NewGuid().ToString('N') + [Guid]::NewGuid().ToString('N')
Set-Content -LiteralPath $passwordFile -Value $testPassword -Encoding ascii
$oldConnection = $env:ConnectionStrings__MoneyMate
$oldPassword = $env:PGPASSWORD
$oldCliHome = $env:DOTNET_CLI_HOME
$oldPackages = $env:NUGET_PACKAGES
$oldEnvironment = $env:ASPNETCORE_ENVIRONMENT
$webProcess = $null
$started = $false
function Wait-TestWeb {
    for ($attempt = 0; $attempt -lt 50; $attempt++) {
        try { $null = Invoke-RestMethod 'http://127.0.0.1:5078/health'; return }
        catch { Start-Sleep -Milliseconds 200 }
    }
    throw 'Test web server did not start on port 5078.'
}
function Assert-DatabaseStatus([string]$expected) {
    $actual = Invoke-RestMethod 'http://127.0.0.1:5078/api/development/database-status'
    if ($actual.status -ne $expected) { throw "Expected $expected, received $($actual.status)" }
}
function Assert-StatisticsOutage($session) {
    & (Join-Path $PostgresBin 'pg_ctl.exe') -D $dataPath -m fast -w stop
    if ($LASTEXITCODE -ne 0) { throw 'Cannot stop isolated DB for statistics outage check.' }
    try {
        $api = Send-AccountRequest $session GET '/api/statistics/monthly'
        Assert-AccountStatus $api 503 'Statistics DB outage'
        if ($api.Data.code -ne 'statistics_unavailable' -or $api.Text.Contains('Npgsql') -or $api.Text.Contains('Password=')) { throw 'Statistics failure response leaked internals or used wrong error code.' }
        $page = Send-AccountRequest $session GET '/Dashboard'
        Assert-AccountStatus $page 503 'Dashboard DB outage'
        if (-not $page.Text.Contains('role="alert"') -or $page.Text.Contains('stat-card') -or $page.Text.Contains('Npgsql')) { throw 'Dashboard outage showed fake totals or failed to provide safe feedback.' }
        $homeResponse = Send-AccountRequest $session GET '/'
        Assert-AccountStatus $homeResponse 200 'Authenticated home during DB outage'
        if ($homeResponse.Text.Contains('0원</b>') -or $homeResponse.Text.Contains('Npgsql')) { throw 'Home outage showed fake zero totals or internals.' }
        $null = Invoke-RestMethod 'http://127.0.0.1:5078/health'
    } finally {
        & (Join-Path $PostgresBin 'pg_ctl.exe') -D $dataPath -l (Join-Path $testRoot 'postgres.log') -o '-p 55432 -h 127.0.0.1' -w start
        if ($LASTEXITCODE -ne 0) { throw 'Cannot restart isolated DB after statistics outage check.' }
    }
    Assert-AccountStatus (Send-AccountRequest $session GET '/api/statistics/monthly') 200 'Statistics recovery'
    Write-Host 'PASS: statistics API/dashboard 503 without internals/fake zeros, home/health survival, DB recovery.'
}
try {
    & (Join-Path $PostgresBin 'initdb.exe') -D $dataPath -U test_admin -A scram-sha-256 --pwfile $passwordFile --encoding UTF8 --locale C
    if ($LASTEXITCODE -ne 0) { throw 'Test cluster initialization failed.' }
    & (Join-Path $PostgresBin 'pg_ctl.exe') -D $dataPath -l (Join-Path $testRoot 'postgres.log') -o '-p 55432 -h 127.0.0.1' -w start
    if ($LASTEXITCODE -ne 0) { throw 'Test cluster startup failed; verify port 55432 is available.' }
    $started = $true
    $env:PGPASSWORD = $testPassword
    $psql = Join-Path $PostgresBin 'psql.exe'
    'CREATE DATABASE moneymate_test;' | & $psql -X -w -h 127.0.0.1 -p 55432 -U test_admin -d postgres --set ON_ERROR_STOP=on
    if ($LASTEXITCODE -ne 0) { throw 'Test DB creation failed.' }
    $env:ConnectionStrings__MoneyMate = "Host=127.0.0.1;Port=55432;Database=moneymate_test;Username=test_admin;Password=$testPassword;Timeout=5;Command Timeout=5"
    $env:DOTNET_CLI_HOME = Join-Path $root '.cli-home'
    $env:NUGET_PACKAGES = Join-Path $root '.packages'
    $env:ASPNETCORE_ENVIRONMENT = 'Development'
    $webProcess = Start-Process -FilePath (Get-Command dotnet).Source -ArgumentList @('bin\Debug\net10.0\MoneyMate.dll', '--urls', 'http://127.0.0.1:5078') -WorkingDirectory (Join-Path $root 'MoneyMate') -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $testRoot 'web.log') -RedirectStandardError (Join-Path $testRoot 'web-error.log')
    Wait-TestWeb
    Assert-DatabaseStatus 'migration_required'
    Push-Location (Join-Path $root 'MoneyMate')
    try {
        & dotnet ef database update --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Migration application failed.' }
        & dotnet ef migrations has-pending-model-changes --no-build
        if ($LASTEXITCODE -ne 0) { throw 'Model and migration snapshot differ.' }
    } finally { Pop-Location }
    Assert-DatabaseStatus 'ready'
    & (Join-Path $PSScriptRoot 'Test-Accounts.ps1')
    $sql = @'
DO $$
BEGIN
  IF (SELECT count(*) FROM "Categories") <> 17 THEN
    RAISE EXCEPTION 'Expected 17 default categories';
  END IF;
  IF (SELECT count(*) FROM "UserLedgerStates") <> 3 THEN
    RAISE EXCEPTION 'Expected ledger state for all three registered accounts';
  END IF;
  IF EXISTS (SELECT 1 FROM "AspNetUsers" WHERE "PasswordHash" IS NULL OR length("PasswordHash") < 60) THEN
    RAISE EXCEPTION 'Expected hashed passwords';
  END IF;
  IF NOT EXISTS (SELECT 1 FROM "AspNetUsers" WHERE "Email" = 'account-a@example.test' AND "LockoutEnd" > now()) THEN
    RAISE EXCEPTION 'Expected persisted login lockout';
  END IF;
  IF (SELECT s."DataVersion" FROM "UserLedgerStates" s JOIN "AspNetUsers" u ON u."Id"=s."UserId" WHERE u."Email"='account-a@example.test') <> 17 THEN
    RAISE EXCEPTION 'Expected all 17 successful transaction mutations to increment the owner ledger version';
  END IF;
  IF (SELECT s."DataVersion" FROM "UserLedgerStates" s JOIN "AspNetUsers" u ON u."Id"=s."UserId" WHERE u."Email"='account-b@example.test') <> 0 THEN
    RAISE EXCEPTION 'Foreign mutation attempts changed another owner ledger version';
  END IF;
END $$;
INSERT INTO "AspNetUsers" ("Id", "DisplayName", "CreatedAt", "EmailConfirmed", "PhoneNumberConfirmed", "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount")
VALUES ('test-user', 'Test', now(), false, false, false, false, 0);
INSERT INTO "Transactions" ("Id", "UserId", "Type", "Amount", "CategoryId", "TransactionDate", "Title", "Version", "CreatedAt", "UpdatedAt")
VALUES ('00000000-0000-4000-8000-000000000001', 'test-user', 2, 12000, 1, '2026-09-02', 'Lunch', 1, now(), now());
DO $$
BEGIN
  BEGIN
    UPDATE "Transactions" SET "Amount" = 0;
    RAISE EXCEPTION 'Zero amount was incorrectly accepted';
  EXCEPTION WHEN check_violation THEN NULL;
  END;
  BEGIN
    UPDATE "Transactions" SET "CategoryId" = 101;
    RAISE EXCEPTION 'Income category for expense was incorrectly accepted';
  EXCEPTION WHEN foreign_key_violation THEN NULL;
  END;
  BEGIN
    UPDATE "Transactions" SET "UserId" = 'missing-user';
    RAISE EXCEPTION 'Missing owner was incorrectly accepted';
  EXCEPTION WHEN foreign_key_violation THEN NULL;
  END;
  IF (SELECT "Amount" FROM "Transactions" LIMIT 1) <> 12000 THEN
    RAISE EXCEPTION 'Valid transaction was changed by a rejected operation';
  END IF;
END $$;
'@
    $sql | & $psql -X -w -h 127.0.0.1 -p 55432 -U test_admin -d moneymate_test --set ON_ERROR_STOP=on
    if ($LASTEXITCODE -ne 0) { throw 'Database invariant verification failed.' }
    & (Join-Path $PostgresBin 'pg_ctl.exe') -D $dataPath -m fast -w stop
    if ($LASTEXITCODE -ne 0) { throw 'Test cluster shutdown failed.' }
    $started = $false
    Assert-DatabaseStatus 'unavailable'
    if ((Invoke-RestMethod 'http://127.0.0.1:5078/health').status -ne 'ok') { throw 'DB failure affected web health.' }
    Write-Host 'PASS: migration, model snapshot, category seeds, valid transaction, amount/category/owner constraints, HTTP migration_required/ready/unavailable, web health during DB outage.'
} finally {
    if ($webProcess -and -not $webProcess.HasExited) { Stop-Process -Id $webProcess.Id }
    if ($started) { & (Join-Path $PostgresBin 'pg_ctl.exe') -D $dataPath -m fast -w stop }
    # Remove only this known ephemeral credential file; retain test cluster/logs for diagnostics.
    Remove-Item -LiteralPath $passwordFile -ErrorAction SilentlyContinue
    $env:ConnectionStrings__MoneyMate = $oldConnection
    $env:PGPASSWORD = $oldPassword
    $env:DOTNET_CLI_HOME = $oldCliHome
    $env:NUGET_PACKAGES = $oldPackages
    $env:ASPNETCORE_ENVIRONMENT = $oldEnvironment
    $testPassword = $null
}

