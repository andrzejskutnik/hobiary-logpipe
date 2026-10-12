param(
    [string]$ConsolePluginId = "brokerless-demo",
    [string]$WebPluginId = "brokerless-web-demo",
    [string]$ConsoleLogDir = ".\samples\BrokerlessDemo\demo-logs",
    [string]$WebLogDir = ".\samples\BrokerlessWebDemo\demo-logs",
    [string]$WebUrl = "http://localhost:5088",
    [int]$WarmupSeconds = 5,
    [string]$NativeBuildDirectory = "",
    [switch]$StopWebOnExit
)

$ErrorActionPreference = "Stop"
if ($WarmupSeconds -lt 1) { throw "WarmupSeconds must be positive." }
$uri = [Uri]$WebUrl
if (-not $uri.IsLoopback -or $uri.Scheme -ne "http") {
    throw "WebUrl must be a loopback HTTP URL."
}

$repoRoot = Split-Path -Parent $PSScriptRoot
$consoleProject = Join-Path $repoRoot "samples\BrokerlessDemo\BrokerlessDemo.csproj"
$webProject = Join-Path $repoRoot "samples\BrokerlessWebDemo\BrokerlessWebDemo.csproj"
$expectedConsoleLogRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "samples\BrokerlessDemo\demo-logs"))
$expectedWebLogRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot "samples\BrokerlessWebDemo\demo-logs"))
$consoleLogRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $ConsoleLogDir))
$webLogRoot = [System.IO.Path]::GetFullPath((Join-Path $repoRoot $WebLogDir))

foreach ($pluginId in @($ConsolePluginId, $WebPluginId)) {
    if ($pluginId -notmatch '^[a-zA-Z0-9_.-]{1,64}$' -or $pluginId -in @(".", "..")) {
        throw "Plugin IDs must match [a-zA-Z0-9_.-]{1,64} and cannot be '.' or '..'."
    }
}

if (-not [string]::Equals($consoleLogRoot, $expectedConsoleLogRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "ConsoleLogDir must resolve to '$expectedConsoleLogRoot' because the script clears the selected plugin directory."
}

if (-not [string]::Equals($webLogRoot, $expectedWebLogRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "WebLogDir must resolve to '$expectedWebLogRoot' because the script clears the selected plugin directory."
}

$consolePluginDirectory = Join-Path $consoleLogRoot $ConsolePluginId
$webPluginDirectory = Join-Path $webLogRoot $WebPluginId

foreach ($path in @($consolePluginDirectory, $webPluginDirectory)) {
    if (Test-Path $path) {
        Remove-Item -Path $path -Recurse -Force
    }
}

$webEnv = @{
    HOBIARY_PLUGIN_ID = $WebPluginId
    HOBIARY_DIRECT_LOG_DIR = $webLogRoot
    HOBIARY_WEB_URL = $WebUrl
}

$buildArgs = @("build", (Join-Path $repoRoot "HobiaryLogPipe.slnx"), "-c", "Release")
if ($NativeBuildDirectory) { $buildArgs += "-p:HobiaryLogDbBuildDir=$NativeBuildDirectory" }
& dotnet @buildArgs
if ($LASTEXITCODE -ne 0) { throw "Demo build failed with exit code $LASTEXITCODE." }
$webDll = Join-Path $repoRoot "samples\BrokerlessWebDemo\bin\Release\net10.0\BrokerlessWebDemo.dll"
$consoleDll = Join-Path $repoRoot "samples\BrokerlessDemo\bin\Release\net10.0\BrokerlessDemo.dll"
$webProcess = Start-Process -FilePath "dotnet" -ArgumentList "`"$webDll`"" -WorkingDirectory $repoRoot -PassThru -Environment $webEnv

try {
    $rootEndpoint = "$WebUrl/"
    $recentEndpoint = "$WebUrl/logs/recent?limit=20&minutes=30"
    $htmlEndpoint = "$WebUrl/logs/view?limit=20&minutes=30"

    for ($attempt = 0; $attempt -lt $WarmupSeconds * 4; $attempt++) {
        try {
            Invoke-WebRequest -Uri $rootEndpoint | Out-Null
            break
        }
        catch {
            if ($webProcess.HasExited) {
                throw "BrokerlessWebDemo exited early with code $($webProcess.ExitCode)."
            }

            if ($attempt -eq ($WarmupSeconds * 4 - 1)) {
                throw "BrokerlessWebDemo did not start listening at $WebUrl within ${WarmupSeconds}s."
            }

            Start-Sleep -Milliseconds 250
        }
    }

    Write-Host "BrokerlessWebDemo is running under PID $($webProcess.Id)."
    Write-Host "Invoking sample HTTP requests..."

    Invoke-RestMethod -Uri $rootEndpoint | ConvertTo-Json -Depth 5
    $order = Invoke-RestMethod -Uri "$WebUrl/orders/42"
    if ($order.id -ne 42 -or $order.transport -ne "Direct") { throw "Unexpected order response." }
    $order | ConvertTo-Json -Depth 5
    $failResponse = Invoke-WebRequest -Uri "$WebUrl/fail" -SkipHttpErrorCheck
    if ($failResponse.StatusCode -ne 500) { throw "Synthetic failure must return HTTP 500." }
    Write-Host "GET /fail -> HTTP $($failResponse.StatusCode)"

    Write-Host "Running console brokerless demo..."
    dotnet $consoleDll --plugin-id $ConsolePluginId --log-dir $consoleLogRoot --hold-seconds 1
    if ($LASTEXITCODE -ne 0) { throw "Console demo failed with exit code $LASTEXITCODE." }

    Write-Host "Recent web demo records:"
    $verified = $false
    for ($attempt = 0; $attempt -lt 40; $attempt++) {
        $recent = Invoke-RestMethod -Uri $recentEndpoint
        $hasOrder = @($recent.items | Where-Object { $_.payload -like "*Loaded order 42 in brokerless web demo*" }).Count -gt 0
        $hasError = @($recent.items | Where-Object { $_.level -eq "Error" -and $_.payload -like "*GET /fail*" }).Count -gt 0
        if ($recent.pluginId -eq $WebPluginId -and $hasOrder -and $hasError) {
            $verified = $true
            break
        }
        Start-Sleep -Milliseconds 250
    }
    if (-not $verified) { throw "Web readback did not contain both the order event and HTTP failure." }
    $recent | ConvertTo-Json -Depth 6
    $html = Invoke-WebRequest -Uri $htmlEndpoint
    if ($html.Content -notlike "*Loaded order 42*" -or $html.Content -notlike "*GET /fail*") {
        throw "HTML view did not render the verified demo records."
    }
    Write-Host "PASS: console readback, order event, HTTP 500 log and HTML view verified."
    Write-Host "HTML view: $htmlEndpoint"

    if ($StopWebOnExit) {
        Stop-Process -Id $webProcess.Id -Force
        Write-Host "BrokerlessWebDemo stopped automatically."
    }
    else {
        Write-Host "Web demo remains running. Stop it with: Stop-Process -Id $($webProcess.Id)"
    }
}
catch {
    if (-not $webProcess.HasExited) {
        Stop-Process -Id $webProcess.Id -Force
    }

    throw
}