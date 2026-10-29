$ErrorActionPreference = "Stop"
$running = Get-Process -Name "SlayTheSpire2" -ErrorAction SilentlyContinue
if ($running) {
    Write-Host "检测到游戏正在运行。请先完全退出游戏，再运行安装（否则 DLL 被占用会失败）。" -ForegroundColor Red
    exit 1
}
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$modName = "SpireCompanion"

function Find-GameDir {
    $candidates = @()
    $steam = (Get-ItemProperty -Path "HKCU:\Software\Valve\Steam" -ErrorAction SilentlyContinue).SteamPath
    if ($steam) {
        $vdf = Join-Path $steam "steamapps\libraryfolders.vdf"
        if (Test-Path $vdf) {
            foreach ($line in Get-Content $vdf) {
                if ($line -match '"path"\s+"(.+?)"') { $candidates += (Join-Path $matches[1] "steamapps\common\Slay the Spire 2") }
            }
        }
        $candidates += (Join-Path $steam "steamapps\common\Slay the Spire 2")
    }
    $candidates += "C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2"
    $candidates += "D:\SteamLibrary\steamapps\common\Slay the Spire 2"
    foreach ($dir in $candidates) { if (Test-Path (Join-Path $dir "SlayTheSpire2.exe")) { return $dir } }
    return $null
}

$gameDir = Find-GameDir
if (-not $gameDir) {
    Write-Host "没有自动找到游戏目录。" -ForegroundColor Red
    $gameDir = Read-Host "请粘贴游戏目录（包含 SlayTheSpire2.exe 的文件夹）"
    $gameDir = $gameDir.Trim('"')
}
if (-not (Test-Path (Join-Path $gameDir "SlayTheSpire2.exe"))) {
    Write-Host "这个目录里没有 SlayTheSpire2.exe，请检查后重试。" -ForegroundColor Red
    exit 1
}

$mods = Join-Path $gameDir "mods"
if (-not (Test-Path $mods)) { New-Item -ItemType Directory -Path $mods | Out-Null }

$source = Join-Path $here $modName
if (-not (Test-Path $source)) {
    Write-Host "安装包缺少 $modName 文件夹，请重新解压完整压缩包。" -ForegroundColor Red
    exit 1
}
$target = Join-Path $mods $modName
if (Test-Path $target) { Remove-Item -Path $target -Recurse -Force }
Copy-Item -Path $source -Destination $target -Recurse -Force
Write-Host "已安装到: $target" -ForegroundColor Green

$solver = Test-Path (Join-Path $mods "CombatSolver\CombatSolver.json")
$ritsu = @(Get-ChildItem -Path $mods -Directory -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "*RitsuLib*" }).Count -gt 0
if (-not $ritsu) {
    Write-Host "缺少 RitsuLib：CombatSolver 需要它。请在 Steam 创意工坊搜索并订阅 RitsuLib。" -ForegroundColor Yellow
}
if (-not $solver) {
    Write-Host "缺少 CombatSolver：不装它战斗不会自动打，只能聊天和局外自动。" -ForegroundColor Yellow
    Write-Host "请在 Steam 创意工坊搜索 CombatSolver（战斗路线求解器）并订阅。" -ForegroundColor Yellow
}
if ($solver -and $ritsu) { Write-Host "依赖检查通过：CombatSolver + RitsuLib 已在 mods 目录。" -ForegroundColor Green }
Write-Host ""
Write-Host "下一步：启动游戏，确认 Mod 列表里 尖塔 AI 陪玩（开发版）已启用，进游戏按 F9。" -ForegroundColor Cyan
