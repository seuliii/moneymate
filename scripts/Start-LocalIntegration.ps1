# Windows local development only. Keep this terminal open; Ctrl+C stops both services.
[CmdletBinding()]
param(
    [ValidateRange(1024,65535)][int]$WebPort = 5077,
    [ValidateRange(1024,65535)][int]$AnalysisPort = 5090,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$pythonPath = Join-Path $root 'analysis\.venv\Scripts\python.exe'
$webRoot = Join-Path $root 'MoneyMate'
$dll = Join-Path $webRoot 'bin\Debug\net10.0\MoneyMate.dll'
if ($WebPort -eq $AnalysisPort) { throw 'WebPort and AnalysisPort must differ.' }
if (-not (Test-Path -LiteralPath $pythonPath)) {
    throw 'Python virtual environment missing. Follow docs/local-integration.md first.'
}
$dotnetPath = (Get-Command dotnet -ErrorAction Stop).Source
foreach ($port in @($WebPort, $AnalysisPort)) {
    $listener = New-Object System.Net.Sockets.TcpListener([Net.IPAddress]::Loopback, $port)
    try { $listener.Start() }
    catch { throw "Port $port is already in use. Stop the existing service or choose another port." }
    finally { $listener.Stop() }
}

$names = @('ANALYSIS_SERVICE_KEY', 'Analysis__ServiceKey', 'Analysis__Mode', 'Analysis__BaseUrl',
    'Analysis__ModelKey', 'ASPNETCORE_ENVIRONMENT', 'DOTNET_CLI_HOME', 'NUGET_PACKAGES')
$previous = @{}
foreach ($name in $names) { $previous[$name] = [Environment]::GetEnvironmentVariable($name, 'Process') }
$pythonProcess = $null
$webProcess = $null
$logDir = Join-Path $root ('artifacts\local-integration\run-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $logDir -Force | Out-Null

function Stop-OwnedProcess($process) {
    if ($null -ne $process -and -not $process.HasExited) {
        # The venv launcher may own a Python child; terminate this launch's entire process tree.
        try {
            & "$env:SystemRoot\System32\taskkill.exe" /PID $process.Id /T /F 2>$null | Out-Null
            if ($LASTEXITCODE -ne 0 -and -not $process.HasExited) {
                Write-Warning "Could not stop process $($process.Id). Check the service manually."
            }
        }
        catch { Write-Warning "Could not stop process $($process.Id). Check the service manually." }
    }
}

try {
    try {
        $env:DOTNET_CLI_HOME = Join-Path $root '.cli-home'
        $env:NUGET_PACKAGES = Join-Path $root '.packages'
        if (-not $SkipBuild) {
            & $dotnetPath build (Join-Path $root 'MoneyMate\MoneyMate.csproj') --nologo
            if ($LASTEXITCODE -ne 0) { throw 'C# build failed.' }
        }
        if (-not (Test-Path -LiteralPath $dll)) { throw 'Build output missing. Run without -SkipBuild.' }
        $keyBytes = New-Object byte[] 32
        $random = [Security.Cryptography.RandomNumberGenerator]::Create()
        try { $random.GetBytes($keyBytes) } finally { $random.Dispose() }
        $env:ANALYSIS_SERVICE_KEY = [Convert]::ToBase64String($keyBytes)
        $env:Analysis__ServiceKey = $env:ANALYSIS_SERVICE_KEY
        $env:Analysis__Mode = 'Http'
        $env:Analysis__BaseUrl = "http://127.0.0.1:$AnalysisPort/"
        # Keep stub caches separate from future LLM reports.
        $env:Analysis__ModelKey = 'partner-stub-local-v1'
        $env:ASPNETCORE_ENVIRONMENT = 'Development'
        $pythonProcess = Start-Process -FilePath $pythonPath -WorkingDirectory (Join-Path $root 'analysis') `
            -ArgumentList "-m uvicorn app.main:create_app --factory --host 127.0.0.1 --port $AnalysisPort" `
            -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'python.out.log') `
            -RedirectStandardError (Join-Path $logDir 'python.err.log')
        $webProcess = Start-Process -FilePath $dotnetPath -WorkingDirectory $webRoot `
            -ArgumentList "bin/Debug/net10.0/MoneyMate.dll --urls http://localhost:$WebPort" `
            -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $logDir 'web.out.log') `
            -RedirectStandardError (Join-Path $logDir 'web.err.log')
    }
    finally {
        foreach ($name in $names) { [Environment]::SetEnvironmentVariable($name, $previous[$name], 'Process') }
    }

    $ready = $false
    for ($attempt = 0; $attempt -lt 30; $attempt++) {
        if ($pythonProcess.HasExited -or $webProcess.HasExited) { throw "A service exited. Check logs: $logDir" }
        try {
            $null = Invoke-RestMethod "http://127.0.0.1:$AnalysisPort/openapi.json" -TimeoutSec 2
            $null = Invoke-RestMethod "http://localhost:$WebPort/health" -TimeoutSec 2
            $ready = $true
            break
        }
        catch { Start-Sleep -Milliseconds 500 }
    }
    if (-not $ready) { throw "Service startup timed out. Check logs: $logDir" }
    $database = Invoke-RestMethod "http://localhost:$WebPort/api/development/database-status" -TimeoutSec 10
    if ($database.status -ne 'ready') { throw 'Database is not ready. Check local DB configuration and migrations.' }
    Write-Host "Web: http://localhost:$WebPort/Account/Login"
    Write-Host "Python: http://127.0.0.1:$AnalysisPort/docs"
    Write-Host 'Mode: HTTP / rule-based stub (no LLM). Database: ready.'
    Write-Host "Logs: $logDir"
    Write-Host 'Keep this terminal open. Press Ctrl+C to stop both services.'
    while (-not $pythonProcess.HasExited -and -not $webProcess.HasExited) { Start-Sleep -Seconds 1 }
    throw "A service stopped. Check logs: $logDir"
}
finally {
    try { Stop-OwnedProcess $webProcess }
    finally { Stop-OwnedProcess $pythonProcess }
}
