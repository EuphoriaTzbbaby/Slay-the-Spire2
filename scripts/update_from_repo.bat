@echo off
chcp 65001 >nul
cd /d "%~dp0.."
echo 正在更新仓库 ...
git pull --ff-only
if errorlevel 1 (
  echo git pull 失败：请检查网络、分支和权限。
  pause
  exit /b 1
)
for /f "usebackq delims=" %%D in (`powershell -NoProfile -Command "$steam=(Get-ItemProperty 'HKCU:\Software\Valve\Steam' -ErrorAction SilentlyContinue).SteamPath; $c=@(); if($steam){$c+=(Join-Path $steam 'steamapps\common\Slay the Spire 2')}; $c+='C:\Program Files (x86)\Steam\steamapps\common\Slay the Spire 2'; foreach($d in $c){if(Test-Path (Join-Path $d 'SlayTheSpire2.exe')){Write-Output $d; break}}"`) do set "GAMEDIR=%%D"
if not defined GAMEDIR set /p GAMEDIR=没有自动找到游戏目录，请粘贴包含 SlayTheSpire2.exe 的文件夹:
set "GAMEDIR=%GAMEDIR:"=%"
if not exist "%GAMEDIR%\SlayTheSpire2.exe" (
  echo 游戏目录无效：%GAMEDIR%
  pause
  exit /b 1
)
echo 正在编译并安装到 %GAMEDIR%\mods ...
python scripts\game_dev.py run --no-launch --game-data "%GAMEDIR%\data_sts2_windows_x86_64" --mods-dir "%GAMEDIR%\mods"
if errorlevel 1 (
  echo 编译或安装失败，请把上面的报错发出来。
  pause
  exit /b 1
)
echo 完成。请重启游戏。
pause
