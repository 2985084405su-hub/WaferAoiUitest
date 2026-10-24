# 构建并发布 01—07 节真实执行包。
#
# 本机对源码有透明加密/过滤：csc 会把 .cs 当成二进制（CS2015），.NET CLI 也无法写入受保护目录。
# 因此固定流程是：同步到可写镜像 → 用 Visual Studio 的 MSBuild 构建/发布 → 复制回仓库 dist。
#
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File tools\build-release.ps1
#
# 可选参数：
#   -Mirror <dir>   构建镜像目录（默认 %USERPROFILE%\Documents\deepseek-harness\default-workspace\mirror\backend）
#   -SkipFrontend   不合并 frontend\dist（前端未构建时使用）

param(
    [string]$Mirror = "$env:USERPROFILE\Documents\deepseek-harness\default-workspace\mirror\backend",
    [switch]$SkipFrontend
)

$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$msbuildCandidates = @(
    'C:\Program Files\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe',
    'C:\Program Files\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe'
)
$msbuild = $msbuildCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $msbuild) { throw '未找到 Visual Studio 2022 的 MSBuild.exe；请安装 VS 2022 或改用已授权的构建机。' }

Write-Host "[1/5] 同步源码到构建镜像：$Mirror"
New-Item -ItemType Directory -Force -Path $Mirror | Out-Null
$robocopy = Start-Process -FilePath 'robocopy' -ArgumentList @(
    "`"$repo\backend`"", "`"$Mirror`"", '/MIR', '/XD', 'obj', 'bin', '.vs', 'dist',
    '/NFL', '/NDL', '/NJH', '/NJS', '/R:1', '/W:1'
) -Wait -PassThru -NoNewWindow
if ($robocopy.ExitCode -ge 8) { throw "robocopy 失败：$($robocopy.ExitCode)" }

Write-Host '[2/5] 构建 Release'
$buildLog = Join-Path $Mirror 'release-build.log'
& $msbuild "$Mirror\WaferAoi.UiTestTool.sln" /nologo /v:m /restore /p:Configuration=Release *> $buildLog
if ($LASTEXITCODE -ne 0) {
    Select-String -Path $buildLog -Pattern ': error ' | ForEach-Object { $_.Line }
    throw 'Release 构建失败'
}

Write-Host '[3/5] 发布 Web 宿主与执行器'
$dist = Join-Path $Mirror 'dist'
if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Force -Path $dist | Out-Null
& $msbuild "$Mirror\src\WaferAoi.UiTestTool.Web\WaferAoi.UiTestTool.Web.csproj" /nologo /v:q /t:Publish `
    /p:Configuration=Release /p:PublishDir="$dist\" *> (Join-Path $Mirror 'publish-web.log')
if ($LASTEXITCODE -ne 0) { throw 'Web 发布失败' }
& $msbuild "$Mirror\src\WaferAoi.UiTestTool.Executor\WaferAoi.UiTestTool.Executor.csproj" /nologo /v:q /t:Publish `
    /p:Configuration=Release /p:PublishDir="$dist\executor\" *> (Join-Path $Mirror 'publish-executor.log')
if ($LASTEXITCODE -ne 0) { throw '执行器发布失败' }

Write-Host '[4/5] 合并前端构建产物'
if (-not $SkipFrontend) {
    if (Test-Path "$repo\frontend\dist") {
        Copy-Item "$repo\frontend\dist" (Join-Path $dist 'web') -Recurse -Force
    } else {
        Write-Warning '未找到 frontend\dist；页面不可用，请先执行 npm run build，或加 -SkipFrontend。'
    }
}

Write-Host '[5/5] 复制到仓库 dist'
$target = Join-Path $repo 'dist'
if (Test-Path $target) { Remove-Item -Recurse -Force $target }
Copy-Item $dist $target -Recurse -Force

$starter = @'
@echo off
rem 主流程 01—07 节真实执行面板（仅本机回环 127.0.0.1:5180）
rem 现场密码只从环境变量读取，不落盘；请先设置 WAFER_AOI_UI_PASSWORD。
setlocal
cd /d "%~dp0"
if "%WAFER_AOI_UI_PASSWORD%"=="" (
  echo [提示] 未设置 WAFER_AOI_UI_PASSWORD：真实运行会在启动前检查处阻塞并写明原因。
)
start "" http://127.0.0.1:5180
WaferAoi.UiTestTool.Web.exe --port 5180 --web-root "%~dp0web" --executor "%~dp0executor\WaferAoi.UiTestTool.Executor.exe"
endlocal
'@
[IO.File]::WriteAllText((Join-Path $target 'start-real.cmd'), $starter, (New-Object Text.UTF8Encoding($false)))

$simStarter = @'
@echo off
rem 契约联调用模拟模式：不启动被测程序、不操作设备，结论不得当作实机通过。
setlocal
cd /d "%~dp0"
start "" http://127.0.0.1:5181
WaferAoi.UiTestTool.Web.exe --port 5181 --web-root "%~dp0web" --executor "%~dp0executor\WaferAoi.UiTestTool.Executor.exe" --simulate
endlocal
'@
[IO.File]::WriteAllText((Join-Path $target 'start-simulate.cmd'), $simStarter, (New-Object Text.UTF8Encoding($false)))

$files = (Get-ChildItem -Recurse -File $target | Measure-Object).Count
$size = [math]::Round(((Get-ChildItem -Recurse -File $target | Measure-Object Length -Sum).Sum / 1MB), 1)
Write-Host "发布完成：$target（$files 个文件，约 $size MB）"
