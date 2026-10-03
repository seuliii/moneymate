# Run locally; the PostgreSQL administrator password is never printed or written to disk.
[CmdletBinding()]
param([string]$PostgresBin = 'C:\Program Files\PostgreSQL\18\bin')

$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$webRoot = Join-Path $projectRoot 'MoneyMate'
$settingsPath = Join-Path $webRoot 'appsettings.Local.json'
$psql = Join-Path $PostgresBin 'psql.exe'
if (-not (Test-Path -LiteralPath $psql)) { throw 'PostgreSQL psql.exe를 찾지 못했습니다.' }
if (Test-Path -LiteralPath $settingsPath) { throw '로컬 설정 파일이 이미 있습니다. README의 수동 설정 안내를 사용하세요.' }

$securePassword = Read-Host '설치 시 설정한 postgres 관리자 비밀번호' -AsSecureString
$passwordPointer = [Runtime.InteropServices.Marshal]::SecureStringToBSTR($securePassword)
$previousPassword = $env:PGPASSWORD
$previousCliHome = $env:DOTNET_CLI_HOME
$previousPackages = $env:NUGET_PACKAGES
try {
    $env:PGPASSWORD = [Runtime.InteropServices.Marshal]::PtrToStringBSTR($passwordPointer)
    $existing = "SELECT (SELECT count(*) FROM pg_roles WHERE rolname = 'moneymate_app') + (SELECT count(*) FROM pg_database WHERE datname = 'moneymate');" |
        & $psql -X -w -h localhost -p 5432 -U postgres -d postgres -t -A --set ON_ERROR_STOP=on
    if ($LASTEXITCODE -ne 0) { throw '관리자 연결에 실패했습니다. 비밀번호와 서비스 상태를 확인하세요.' }
    if (($existing | Out-String).Trim() -ne '0') { throw 'moneymate DB 또는 moneymate_app 계정이 이미 있습니다. 기존 항목을 변경하지 않았습니다.' }

    $passwordBytes = New-Object byte[] 32
    $generator = [Security.Cryptography.RandomNumberGenerator]::Create()
    try { $generator.GetBytes($passwordBytes) } finally { $generator.Dispose() }
    $appPassword = -join ($passwordBytes | ForEach-Object { $_.ToString('x2') })
    "CREATE ROLE moneymate_app LOGIN PASSWORD '$appPassword';" |
        & $psql -X -w -h localhost -p 5432 -U postgres -d postgres --set ON_ERROR_STOP=on
    if ($LASTEXITCODE -ne 0) { throw '앱 계정 생성에 실패했습니다.' }
    "CREATE DATABASE moneymate OWNER moneymate_app;" |
        & $psql -X -w -h localhost -p 5432 -U postgres -d postgres --set ON_ERROR_STOP=on
    if ($LASTEXITCODE -ne 0) { throw 'DB 생성에 실패했습니다. 생성된 moneymate_app 계정은 유지됩니다. pgAdmin에서 상태를 확인하세요.' }

    @{ ConnectionStrings = @{ MoneyMate = "Host=localhost;Port=5432;Database=moneymate;Username=moneymate_app;Password=$appPassword;Timeout=5;Command Timeout=5" } } |
        ConvertTo-Json -Depth 3 | Set-Content -LiteralPath $settingsPath -Encoding UTF8
    # This file contains a generated app credential; .gitignore excludes it.
    # postgres administrator credentials are never stored in it.
    $env:PGPASSWORD = $previousPassword
    $env:DOTNET_CLI_HOME = Join-Path $projectRoot '.cli-home'
    $env:NUGET_PACKAGES = Join-Path $projectRoot '.packages'
    Push-Location $projectRoot
    try {
        & dotnet tool restore
        if ($LASTEXITCODE -ne 0) { throw 'EF 도구 복원 실패. DB와 설정은 생성되었습니다. README의 적용 명령으로 계속할 수 있습니다.' }
        Push-Location $webRoot
        try {
            & dotnet ef database update
            if ($LASTEXITCODE -ne 0) { throw '마이그레이션 실패. DB와 설정은 유지됩니다. README의 적용 명령으로 다시 시도하세요.' }
        } finally { Pop-Location }
    } finally { Pop-Location }
    Write-Host 'moneymate DB, 일반 앱 계정, 초기 테이블과 기본 카테고리를 준비했습니다.'
    Write-Host '웹을 실행하고 DB 연결 확인 버튼을 눌러주세요.'
} finally {
    [Runtime.InteropServices.Marshal]::ZeroFreeBSTR($passwordPointer)
    $securePassword.Dispose()
    $env:PGPASSWORD = $previousPassword
    $env:DOTNET_CLI_HOME = $previousCliHome
    $env:NUGET_PACKAGES = $previousPackages
    $appPassword = $null
}
