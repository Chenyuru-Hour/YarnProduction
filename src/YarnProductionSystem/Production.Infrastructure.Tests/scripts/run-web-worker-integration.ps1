param(
    [int]$StartupTimeoutSeconds = 90,
    [int]$SmokeDurationSeconds = 15
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$scriptRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$solutionRoot = Resolve-Path (Join-Path $scriptRoot "..\..")
$logDir = Join-Path $scriptRoot "logs"

if (-not (Test-Path $logDir)) {
    New-Item -Path $logDir -ItemType Directory | Out-Null
}

$webProject = Join-Path $solutionRoot "Production.Web\Production.Web.csproj"
$workerProject = Join-Path $solutionRoot "Production.Worker\Production.Worker.csproj"
$webLog = Join-Path $logDir "web.log"
$webErrLog = Join-Path $logDir "web.err.log"
$workerLog = Join-Path $logDir "worker.log"
$workerErrLog = Join-Path $logDir "worker.err.log"

if (Test-Path $webLog) { Remove-Item $webLog -Force }
if (Test-Path $webErrLog) { Remove-Item $webErrLog -Force }
if (Test-Path $workerLog) { Remove-Item $workerLog -Force }
if (Test-Path $workerErrLog) { Remove-Item $workerErrLog -Force }

function Wait-Until {
    param(
        [scriptblock]$Condition,
        [int]$TimeoutSeconds,
        [int]$PollSeconds = 2,
        [string]$FailureMessage
    )

    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (& $Condition) {
            return
        }

        Start-Sleep -Seconds $PollSeconds
    }

    throw $FailureMessage
}

$webProcess = $null
$workerProcess = $null

try {
    Write-Host "[1/5] 启动 Production.Web..."
    $webProcess = Start-Process -FilePath "dotnet" `
        -ArgumentList @("run", "--project", $webProject, "--launch-profile", "http") `
        -WorkingDirectory $solutionRoot `
        -RedirectStandardOutput $webLog `
        -RedirectStandardError $webErrLog `
        -PassThru

    Write-Host "[2/5] 启动 Production.Worker..."
    $workerProcess = Start-Process -FilePath "dotnet" `
        -ArgumentList @("run", "--project", $workerProject) `
        -WorkingDirectory $solutionRoot `
        -RedirectStandardOutput $workerLog `
        -RedirectStandardError $workerErrLog `
        -PassThru

    Write-Host "[3/5] 等待 Web 监听端口 (http://localhost:5107)..."
    Wait-Until -TimeoutSeconds $StartupTimeoutSeconds -FailureMessage "Web 启动超时。" -Condition {
        if ($null -eq $webProcess -or $webProcess.HasExited) {
            return $false
        }

        if (-not (Test-Path $webLog)) {
            return $false
        }

        $content = Get-Content $webLog -Raw
        return $content -match "Now listening on: http://localhost:5107"
    }

    Write-Host "[4/5] 校验 SignalR Hub negotiate..."
    Wait-Until -TimeoutSeconds 30 -FailureMessage "Hub negotiate 失败。" -Condition {
        try {
            $body = Invoke-RestMethod -Method Post -Uri "http://localhost:5107/hubs/production/negotiate?negotiateVersion=1" -ContentType "application/json" -Body "{}" -TimeoutSec 5
            return $null -ne $body.connectionId
        }
        catch {
            return $false
        }
    }

    Write-Host "[5/5] 观察 $SmokeDurationSeconds 秒并检查日志关键字..."
    Start-Sleep -Seconds $SmokeDurationSeconds

    $workerLogText = if (Test-Path $workerLog) { Get-Content $workerLog -Raw } else { "" }
    $webLogText = if (Test-Path $webLog) { Get-Content $webLog -Raw } else { "" }

    if ($workerLogText -notmatch "Production\.Worker\.Worker\[0\]" -or $workerLogText -notmatch "Application started\. Press Ctrl\+C to shut down\.") {
        throw "Worker 未输出启动日志，请检查 $workerLog"
    }

    if ($webLogText -notmatch "Production\.Web\.Services\.RedisToSignalRForwarder\[0\]" -or $webLogText -notmatch "Now listening on: http://localhost:5107") {
        throw "Web 未输出 Redis 转发启动日志，请检查 $webLog"
    }

    Write-Host "联调成功：Web/Worker 已启动，Hub negotiate 正常，实时转发服务已就绪。"
    Write-Host "Web 日志: $webLog"
    Write-Host "Worker 日志: $workerLog"
}
finally {
    if ($workerProcess -and -not $workerProcess.HasExited) {
        Stop-Process -Id $workerProcess.Id -Force
    }

    if ($webProcess -and -not $webProcess.HasExited) {
        Stop-Process -Id $webProcess.Id -Force
    }
}
