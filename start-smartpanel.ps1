[CmdletBinding()]
param(
    [switch]$NoBuild
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$apiProject = Join-Path $root "SmartPanel.Api"
$webProject = Join-Path $root "SmartPanel.Web"
$apiProjectFile = Join-Path $apiProject "SmartPanel.Api.csproj"
$webProjectFile = Join-Path $webProject "SmartPanel.Web.csproj"
$apiExe = Join-Path $apiProject "bin\Debug\net8.0\SmartPanel.Api.exe"
$webExe = Join-Path $webProject "bin\Debug\net8.0\SmartPanel.Web.exe"
$runlogs = Join-Path $root "runlogs"

$apiUrl = "http://localhost:5203"
$webUrl = "http://localhost:5037"
$apiCheckUrl = "$apiUrl/swagger"
$webCheckUrl = "$webUrl/rooms"

function Stop-PortListeners {
    param([int[]]$Ports)

    foreach ($port in $Ports) {
        $connections = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue
        if ($null -eq $connections) {
            continue
        }

        $processIds = $connections | Select-Object -ExpandProperty OwningProcess -Unique
        foreach ($processId in $processIds) {
            try {
                Stop-Process -Id $processId -Force -ErrorAction Stop
                Write-Host "Stopped process $processId on port $port"
            }
            catch {
                Write-Warning "Failed to stop process $processId on port ${port}: $($_.Exception.Message)"
            }
        }
    }
}

function Invoke-Build {
    param(
        [string]$ProjectFile,
        [string]$Name
    )

    Write-Host "Building $Name..."
    & dotnet build $ProjectFile -v minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Build failed for $Name."
    }
}

function Wait-HttpReady {
    param(
        [string]$Url,
        [string]$Name,
        [int]$TimeoutSeconds = 30
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        try {
            $response = Invoke-WebRequest -UseBasicParsing $Url -TimeoutSec 5
            if ($response.StatusCode -ge 200 -and $response.StatusCode -lt 500) {
                Write-Host "$Name is ready at $Url"
                return
            }
        }
        catch {
        }

        Start-Sleep -Milliseconds 500
    }

    throw "Timed out waiting for $Name at $Url"
}

New-Item -ItemType Directory -Force $runlogs | Out-Null

$oldEnvironment = @{
    ASPNETCORE_ENVIRONMENT = $env:ASPNETCORE_ENVIRONMENT
    ASPNETCORE_URLS = $env:ASPNETCORE_URLS
}

try {
    Stop-PortListeners -Ports @(5203, 5037)
    Stop-Process -Name SmartPanel.Api,SmartPanel.Web -Force -ErrorAction SilentlyContinue

    if (-not $NoBuild) {
        Invoke-Build -ProjectFile $apiProjectFile -Name "SmartPanel.Api"
        Invoke-Build -ProjectFile $webProjectFile -Name "SmartPanel.Web"
    }

    if (-not (Test-Path $apiExe)) {
        throw "Missing API executable: $apiExe"
    }

    if (-not (Test-Path $webExe)) {
        throw "Missing Web executable: $webExe"
    }

    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:ASPNETCORE_URLS = $apiUrl
    $apiProcess = Start-Process -FilePath $apiExe `
        -WorkingDirectory $apiProject `
        -RedirectStandardOutput (Join-Path $runlogs "api.out.log") `
        -RedirectStandardError (Join-Path $runlogs "api.err.log") `
        -WindowStyle Hidden `
        -PassThru

    $env:ASPNETCORE_ENVIRONMENT = "Development"
    $env:ASPNETCORE_URLS = $webUrl
    $webProcess = Start-Process -FilePath $webExe `
        -WorkingDirectory $webProject `
        -RedirectStandardOutput (Join-Path $runlogs "web.out.log") `
        -RedirectStandardError (Join-Path $runlogs "web.err.log") `
        -WindowStyle Hidden `
        -PassThru

    Wait-HttpReady -Url $apiCheckUrl -Name "API"
    Wait-HttpReady -Url $webCheckUrl -Name "Web"

    Write-Host ""
    Write-Host "SmartPanel is running."
    Write-Host "API: $apiUrl/swagger"
    Write-Host "Web: $webUrl"
    Write-Host "API PID: $($apiProcess.Id)"
    Write-Host "Web PID: $($webProcess.Id)"
    Write-Host "Logs: $runlogs"
}
finally {
    $env:ASPNETCORE_ENVIRONMENT = $oldEnvironment.ASPNETCORE_ENVIRONMENT
    $env:ASPNETCORE_URLS = $oldEnvironment.ASPNETCORE_URLS
}
