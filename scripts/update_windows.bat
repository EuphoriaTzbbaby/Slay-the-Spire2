@echo off
chcp 65001 >nul
echo 正在下载最新 Windows 安装包 ...
set "URL=https://github.com/EuphoriaTzbbaby/Slay-the-Spire2/releases/download/latest/SpireCompanion-win64.zip"
set "ZIP=%TEMP%\SpireCompanion-win64.zip"
set "DIR=%TEMP%\SpireCompanion-update"
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='Stop'; Invoke-WebRequest -Uri '%URL%' -OutFile '%ZIP%'; if (Test-Path '%DIR%') { Remove-Item '%DIR%' -Recurse -Force }; Expand-Archive -Path '%ZIP%' -DestinationPath '%DIR%' -Force"
if errorlevel 1 (
  echo 下载或解压失败。若仓库是私有的，需要先用有权限的浏览器登录 GitHub 下载。
  pause
  exit /b 1
)
call "%DIR%\install.bat"
