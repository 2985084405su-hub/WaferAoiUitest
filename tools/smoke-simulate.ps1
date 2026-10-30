# 模拟执行器端到端回归：严格 / 重复提交 / 网页确认 / 执行器被杀 → 待现场处理 → 解除 / SSE 补流 / 历史。
# 用法：先 tools\build-release.ps1 生成 dist，再 powershell -NoProfile -File tools\smoke-simulate.ps1
# 只启动 dist 里的模拟宿主与本机回环端口，不启动被测程序、不接触设备。
$ErrorActionPreference = 'Stop'
[Console]::OutputEncoding = [Text.Encoding]::UTF8

$dist = Join-Path (Split-Path $PSScriptRoot -Parent) 'dist'
$port = 5199
$base = "http://127.0.0.1:$port"
$data = Join-Path $env:TEMP ('wafer-smoke-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
$recipe = Join-Path $env:TEMP ('wafer-recipe-' + [guid]::NewGuid().ToString('N').Substring(0, 8))
New-Item -ItemType Directory -Force -Path $data, $recipe | Out-Null
$env:WAFER_AOI_UI_PASSWORD = 'smoke-not-a-real-password'

$fails = New-Object System.Collections.Generic.List[string]
function Check([string]$name, $cond, [string]$detail = '') {
    if ([bool]$cond) { Write-Host "  PASS $name" } else { Write-Host "  FAIL $name $detail"; $script:fails.Add("$name $detail") }
}
function Get-Json([string]$url) { Invoke-RestMethod -Uri $url -Method Get -TimeoutSec 20 }
function Post-Json([string]$url, $body, [switch]$AllowError) {
    $json = $body | ConvertTo-Json -Depth 6 -Compress
    try {
        return Invoke-RestMethod -Uri $url -Method Post -Body $json -ContentType 'application/json; charset=utf-8' -TimeoutSec 30
    } catch {
        if (-not $AllowError) { throw }
        $resp = $_.Exception.Response
        $code = [int]$resp.StatusCode
        $reader = New-Object IO.StreamReader($resp.GetResponseStream())
        $text = $reader.ReadToEnd()
        return [pscustomobject]@{ __status = $code; __body = $text }
    }
}
function Wait-LockReleased([int]$timeoutSec = 10) {
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        if (-not (Test-Path (Join-Path $data 'lock.json'))) { return $true }
        Start-Sleep -Milliseconds 100
    }
    return $false
}
function Wait-EventFileStable([string]$runId, [int]$timeoutSec = 10) {
    $path = Join-Path $data "runs\$runId\events.jsonl"
    $last = -1
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $count = (Get-Content $path -Encoding UTF8 | Measure-Object).Count
        if ($count -eq $last) { return $count }
        $last = $count
        Start-Sleep -Milliseconds 300
    }
    return $last
}
function Complete-Run([string]$runId, [int]$timeoutSec = 180) {
    $confirmed = 0
    $deadline = (Get-Date).AddSeconds($timeoutSec)
    while ((Get-Date) -lt $deadline) {
        $snap = Get-Json "$base/api/runs/$runId"
        if ($snap.phase -in @('finished', 'pendingField')) { return [pscustomobject]@{ snap = $snap; confirmations = $confirmed } }
        if ($snap.checkpoint -and $snap.checkpoint.status -eq 'waiting') {
            $cpId = $snap.checkpoint.checkpointId
            $r = Post-Json "$base/api/runs/$runId/checkpoints/$cpId/confirm" @{ requestId = "smoke-cp-$cpId"; result = 'done'; note = '模拟确认' }
            Check "确认点 $($snap.checkpoint.stepId) 已接受" ($r.accepted -eq $true)
            $again = Post-Json "$base/api/runs/$runId/checkpoints/$cpId/confirm" @{ requestId = "smoke-cp-$cpId"; result = 'done'; note = '模拟确认' }
            Check "确认点 $($snap.checkpoint.stepId) 重复提交幂等" ($again.replayed -eq $true)
            $confirmed++
        }
        Start-Sleep -Milliseconds 150
    }
    throw "运行未在 $timeoutSec 秒内收敛：$runId"
}

$hostProc = Start-Process -FilePath "$dist\WaferAoi.UiTestTool.Web.exe" -ArgumentList @(
    '--port', "$port", '--data-dir', $data, '--web-root', "$dist\web",
    '--executor', "$dist\executor\WaferAoi.UiTestTool.Executor.exe", '--simulate'
) -PassThru -WindowStyle Hidden

try {
    $ok = $false
    for ($i = 0; $i -lt 60; $i++) {
        Start-Sleep -Milliseconds 250
        try { if ((Get-Json "$base/api/health").status -eq 'ok') { $ok = $true; break } } catch { }
    }
    if (-not $ok) { throw '宿主未在 15 秒内就绪' }
    Write-Host "宿主就绪 pid=$($hostProc.Id) data=$data"

    # 1. 目录与配置
    $catalog = Get-Json "$base/api/catalog/steps"
    Check '目录已实现 45 步' ([int]$catalog.implemented -eq 45) "implemented=$($catalog.implemented)"
    Check '目录步骤数组 45 条' (@($catalog.steps).Count -eq 45) "steps=$(@($catalog.steps).Count)"

    $cfg = @{
        exePath            = 'C:\Windows\System32\notepad.exe'
        recipeDir          = $recipe
        evidenceDir        = (Join-Path $data 'evidence')
        expectedVersion    = ''
        buildDate          = ''
        templateRecipeName = '1'
        userName           = 'admin'
        configurationRoot  = 'C:\Windows'
    }
    Invoke-RestMethod -Uri "$base/api/config" -Method Put -Body ($cfg | ConvertTo-Json) -ContentType 'application/json; charset=utf-8' | Out-Null
    $saved = Get-Json "$base/api/config"
    $gotRecipe = [string]$saved.recipeDir
    $sameRecipe = [string]::Equals([IO.Path]::GetFullPath($gotRecipe.Trim()).TrimEnd('\'), [IO.Path]::GetFullPath($recipe.Trim()).TrimEnd('\'), [StringComparison]::OrdinalIgnoreCase)
    Check '配置可保存回读' $sameRecipe "got=<$gotRecipe> want=<$recipe>"

    # 2. 严格策略：仍保留原用例现场点，但硬件步骤自动执行
    $strict = Post-Json "$base/api/runs" @{ requestId = 'smoke-strict-1'; options = @{ skipVersionAssertion = $true; hardwarePolicy = 'strict' } }
    Check '严格运行已创建' ($strict.created -eq $true)
    $runId = $strict.run.runId
    $replay = Post-Json "$base/api/runs" @{ requestId = 'smoke-strict-1'; options = @{ skipVersionAssertion = $true; hardwarePolicy = 'strict' } }
    Check '重复 requestId 返回同一运行' (($replay.created -eq $false) -and ($replay.run.runId -eq $runId))

    $strictResult = Complete-Run $runId 240
    $snap = $strictResult.snap
    Check '严格运行结束阶段为 finished' ($snap.phase -eq 'finished') "phase=$($snap.phase)"
    Check '严格运行出现原用例现场确认点' ($strictResult.confirmations -ge 3) "confirmations=$($strictResult.confirmations)"
    Check '严格运行计数合计 45' ([int]$snap.counts.total -eq 45) ($snap.counts | ConvertTo-Json -Compress)
    $left = [int]$snap.counts.notRun + [int]$snap.counts.unknown + [int]$snap.counts.waiting
    Check '严格运行无未执行/未知/等待' ($left -eq 0) "left=$left"
    Check '严格运行无待现场处理' ($snap.pendingField.active -eq $false)
    Check '严格运行后机器锁已释放' (Wait-LockReleased 10)
    Check '严格运行结论为 passedWithSkips（跳过版本）' ($snap.conclusion -eq 'passedWithSkips') "conclusion=$($snap.conclusion)"

    $report = Get-Json "$base/api/runs/$runId/report"
    Check '报告步骤数 45' (@($report.steps).Count -eq 45) "count=$(@($report.steps).Count)"
    $stableCount = Wait-EventFileStable $runId 10
    $events = Get-Content (Join-Path $data "runs\$runId\events.jsonl") -Encoding UTF8 | ForEach-Object { $_ | ConvertFrom-Json }
    Check '事件文件在运行结束后停止增长' ($stableCount -eq @($events).Count) "stable=$stableCount read=$(@($events).Count)"
    $seqs = @($events | ForEach-Object { [long]$_.sequence })
    $contiguous = $true
    for ($i = 0; $i -lt $seqs.Count; $i++) { if ($seqs[$i] -ne ($i + 1)) { $contiguous = $false; break } }
    Check '事件序号 1..N 连续' $contiguous "n=$($seqs.Count)"
    $snapFinal = Get-Json "$base/api/runs/$runId"
    Check '事件行数与快照 lastSequence 一致' ($seqs.Count -eq [int]$snapFinal.lastSequence) "events=$($seqs.Count) last=$($snapFinal.lastSequence)"

    $sse = & curl.exe -sN -m 2 "$base/api/runs/$runId/events?after=0" 2>$null
    $sseLines = @($sse | Where-Object { $_ -like 'event:*' })
    Check 'SSE 补流返回全部事件' ($sseLines.Count -eq $seqs.Count) "sse=$($sseLines.Count) file=$($seqs.Count)"

    # 3. 人工策略：硬件点也走网页确认
    $manual = Post-Json "$base/api/runs" @{ requestId = 'smoke-manual-1'; options = @{ skipVersionAssertion = $true; hardwarePolicy = 'manual' } }
    $manualResult = Complete-Run $manual.run.runId 300
    Check '人工策略确认点多于严格策略' ($manualResult.confirmations -gt $strictResult.confirmations) "manual=$($manualResult.confirmations) strict=$($strictResult.confirmations)"
    Check '人工策略结束为 finished' ($manualResult.snap.phase -eq 'finished') "phase=$($manualResult.snap.phase)"
    Check '人工策略计数合计 45' ([int]$manualResult.snap.counts.total -eq 45)
    Check '人工策略无失败/阻塞' ((([int]$manualResult.snap.counts.failed) + ([int]$manualResult.snap.counts.blocked)) -eq 0) ($manualResult.snap.counts | ConvertTo-Json -Compress)

    # 4. 执行器被杀 → 待现场处理 → 占用 → 现场处理 → 解除
    $null = Wait-LockReleased 10
    $crash = Post-Json "$base/api/runs" @{ requestId = 'smoke-crash-1'; options = @{ skipVersionAssertion = $true; hardwarePolicy = 'manual' } }
    $crashId = $crash.run.runId
    $execPid = 0
    $deadline = (Get-Date).AddSeconds(120)
    while ((Get-Date) -lt $deadline) {
        $snap = Get-Json "$base/api/runs/$crashId"
        if (($snap.executor.processId -gt 0) -and ($snap.phase -eq 'waitingHuman')) { $execPid = [int]$snap.executor.processId; break }
        if ($snap.phase -in @('finished', 'pendingField')) { break }
        Start-Sleep -Milliseconds 150
    }
    Check '执行器已启动且有 PID' ($execPid -gt 0) "pid=$execPid phase=$($snap.phase)"
    if ($execPid -gt 0) { Stop-Process -Id $execPid -Force }
    $deadline = (Get-Date).AddSeconds(60)
    do {
        Start-Sleep -Milliseconds 300
        $snap = Get-Json "$base/api/runs/$crashId"
    } while (($snap.phase -ne 'pendingField') -and ((Get-Date) -lt $deadline))
    Check '执行器被杀后进入待现场处理' ($snap.pendingField.active -eq $true) "phase=$($snap.phase)"
    Check '当前步骤结果为未知（不改写为失败）' ([int]$snap.counts.unknown -eq 1) ($snap.counts | ConvertTo-Json -Compress)
    $lock = Get-Content (Join-Path $data 'lock.json') -Raw -Encoding UTF8 | ConvertFrom-Json
    Check '锁记录保持占用且标记待现场处理' (($lock.pendingField -eq $true) -and ($lock.runId -eq $crashId))

    $busy = Post-Json "$base/api/runs" @{ requestId = 'smoke-busy-1'; options = @{ skipVersionAssertion = $true; hardwarePolicy = 'strict' } } -AllowError
    Check '待现场处理期间拒绝新运行(409 machine_busy)' (($busy.__status -eq 409) -and ($busy.__body -match 'machine_busy')) "status=$($busy.__status)"

    # 宿主重启后仍保持占用
    Stop-Process -Id $hostProc.Id -Force
    Start-Sleep -Seconds 1
    $hostProc = Start-Process -FilePath "$dist\WaferAoi.UiTestTool.Web.exe" -ArgumentList @(
        '--port', "$port", '--data-dir', $data, '--web-root', "$dist\web",
        '--executor', "$dist\executor\WaferAoi.UiTestTool.Executor.exe", '--simulate'
    ) -PassThru -WindowStyle Hidden
    for ($i = 0; $i -lt 60; $i++) { Start-Sleep -Milliseconds 250; try { if ((Get-Json "$base/api/health").status -eq 'ok') { break } } catch { } }
    Start-Sleep -Milliseconds 500
    $after = Get-Json "$base/api/runs/$crashId"
    Check '宿主重启后仍为待现场处理' (($after.phase -eq 'pendingField') -and ($after.pendingField.active -eq $true)) "phase=$($after.phase)"
    $current = Get-Json "$base/api/runs/current"
    Check '宿主重启后当前运行指向中断轮' ($current.runId -eq $crashId) "current=$($current.runId)"

    $resolve = Post-Json "$base/api/runs/$crashId/field-resolution" @{ requestId = 'smoke-resolve-1'; processExitedConfirmed = $true; deviceStateConfirmed = $true; note = '模拟现场处理完成' }
    Check '现场处理核验通过' ($resolve.released -eq $true) ($resolve | ConvertTo-Json -Compress -Depth 4)
    Start-Sleep -Milliseconds 600
    $final = Get-Json "$base/api/runs/$crashId"
    Check '解除后阶段改为 finished' ($final.phase -eq 'finished') "phase=$($final.phase)"
    Check '解除后待现场处理关闭' ($final.pendingField.active -eq $false)
    Check '解除后结论保持 interrupted' ($final.conclusion -eq 'interrupted') "conclusion=$($final.conclusion)"
    Check '解除后机器锁已删除' (Wait-LockReleased 10)
    $resolveAgain = Post-Json "$base/api/runs/$crashId/field-resolution" @{ requestId = 'smoke-resolve-2'; processExitedConfirmed = $true; deviceStateConfirmed = $true }
    Check '重复现场处理请求幂等且不再改写结论' ($resolveAgain.released -eq $true)

    # 历史：至少三轮可查
    $history = Get-Json "$base/api/runs?limit=20"
    Check '历史包含至少三轮运行' (@($history.items).Count -ge 3) "items=$(@($history.items).Count)"
}
catch {
    Write-Host "  EXCEPTION $($_.Exception.Message)"
    $fails.Add("EXCEPTION $($_.Exception.Message)")
}
finally {
    if ($hostProc -and -not $hostProc.HasExited) { Stop-Process -Id $hostProc.Id -Force }
    Get-Process -Name 'WaferAoi.UiTestTool.Executor' -ErrorAction SilentlyContinue | Stop-Process -Force
    Write-Host ''
    Write-Host "数据目录：$data"
    if ($fails.Count -eq 0) { Write-Host 'SMOKE RESULT: ALL PASS' } else { Write-Host "SMOKE RESULT: $($fails.Count) FAIL"; $fails | ForEach-Object { Write-Host " - $_" } }
}
