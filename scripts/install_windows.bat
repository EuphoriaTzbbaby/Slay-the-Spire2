@echo off
chcp 65001 >nul
echo 正在安装 尖塔 AI 陪玩 ...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
if errorlevel 1 (
  echo.
  echo 安装失败，请把上面的红色提示截图发给我们。
) else (
  echo.
  echo 安装完成。请启动游戏，在 Mod 列表确认已启用。
)
pause
