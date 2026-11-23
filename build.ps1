<#
    一键编译（含自动部署）。

    真正干拷贝活的是 csproj 里的 DeployPlugin 目标（AfterTargets="Build"），
    所以 dotnet build / IDE 里按编译键也会自动铺到游戏目录和 output/。
    本脚本只是额外做三件事：
      1. 固定用本机的 dotnet8（找不到再退回 PATH 里的 dotnet）；
      2. 编译后校验两处 dll 是否真的和 bin/ 一致（哈希比对）；
      3. 打印版本号、时间戳、失败原因。

    用法：
        .\build.ps1                 编译 + 部署
        .\build.ps1 -Rebuild        强制重编（-t:Rebuild）后再部署
        .\build.ps1 -SkipDeploy     只编译，不拷贝
#>
[CmdletBinding()]
param(
    [switch]$Rebuild,
    [switch]$SkipDeploy
)

$ErrorActionPreference = 'Stop'

# 统一成 UTF-8，避免 msbuild 的中文警告与脚本自己的输出互相串码
try { [Console]::OutputEncoding = [Text.Encoding]::UTF8 } catch { }

$root = Split-Path -Parent $MyInvocation.MyCommand.Definition
$proj = Join-Path $root 'KK_SceneCardFittingToolkit.csproj'
$built = Join-Path $root 'bin\KK_SceneCardFittingToolkit.dll'

$dotnet = 'dotnet'
$pinned = 'D:\game\LL\dotnet8\dotnet.exe'
if (Test-Path $pinned) { $dotnet = $pinned }

$buildArgs = @('build', $proj, '-c', 'Release', '--nologo')
if ($Rebuild) { $buildArgs += '-t:Rebuild' }
if ($SkipDeploy) { $buildArgs += '-p:SkipDeploy=true' }

& $dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    Write-Host "编译失败（exit $LASTEXITCODE）" -ForegroundColor Red
    exit $LASTEXITCODE
}

if ($SkipDeploy) {
    Write-Host "已跳过部署（-SkipDeploy）" -ForegroundColor Yellow
    exit 0
}

# 版本号：读源码常量（dll 没写 Version 资源，FileVersion 是 0.0.0.0）
$version = '(未知)'
$src = Join-Path $root 'src\Core\KKPEHeightLock.cs'
if (Test-Path $src) {
    $hit = Select-String -Path $src -Pattern 'Version\s*=\s*"([^"]+)"' | Select-Object -First 1
    if ($hit) { $version = $hit.Matches[0].Groups[1].Value }
}

$targets = @(
    @{ Name = '游戏目录'; Path = 'D:\game\LL\LL2\K_K_S\BepInEx\plugins\kk_scenecardfittingtoolkit.dll' },
    @{ Name = 'output  '; Path = (Join-Path $root 'output\BepInEx\plugins\kk_scenecardfittingtoolkit.dll') }
)

if (-not (Test-Path $built)) {
    Write-Host "找不到编译产物：$built" -ForegroundColor Red
    exit 1
}

$srcHash = (Get-FileHash $built -Algorithm MD5).Hash
$srcTime = (Get-Item $built).LastWriteTime

Write-Host ''
Write-Host ("版本 {0}    产物 {1:yyyy-MM-dd HH:mm:ss}    {2} 字节" -f $version, $srcTime, (Get-Item $built).Length) -ForegroundColor Cyan

$bad = 0
foreach ($t in $targets) {
    if (-not (Test-Path $t.Path)) {
        Write-Host ("  [{0}] 缺失 → {1}" -f $t.Name, $t.Path) -ForegroundColor Red
        $bad++
        continue
    }
    $same = (Get-FileHash $t.Path -Algorithm MD5).Hash -eq $srcHash
    if ($same) {
        Write-Host ("  [{0}] 已同步 → {1}" -f $t.Name, $t.Path) -ForegroundColor Green
    } else {
        Write-Host ("  [{0}] 不一致（可能被游戏占用，关掉 CharaStudio 再编一次）→ {1}" -f $t.Name, $t.Path) -ForegroundColor Red
        $bad++
    }
}

if ($bad -gt 0) { exit 1 }
exit 0
