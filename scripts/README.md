# 仓库脚本

从任意工作目录执行脚本均使用仓库相对定位。

- check_repository.py：检查目录、子模块映射、JSON 文件与消息公共字段，不访问网络或游戏。

- game_dev.py：build 编译主工程，check 运行独立控制检查，install 将已构建模块安装到指定游戏目录；run 串起构建、安装并启动游戏（macOS，可用 --no-launch 跳过启动）。除 run 外不启动游戏，任何动作都不写入存档。

启动、打包和联调脚本统一放在本目录，旧 Python 原型测试入口已移除。

- game_smoke.py：macOS 独立游戏副本、离线测试账号的加载检查；运行后清理副本和测试账号，保留 .local/smoke 下的日志。验证 Mod 初始化、主菜单状态读取，以及窗口几何（位于视口内、聊天/设置/游戏状态三个页签、滚动区撑开）。

- voice_dev.py：`setup` 创建独立语音环境并下载中文模型；`devices`、`test-mic`、`test-tts` 做真实音频调试；`run` 启动本机语音服务；`check` 跑不采集音频的协议检查。详见 [voice/README.md](../voice/README.md)。

语音环境准备后再次 `game_dev.py install`，会在本机 Mod 目录生成不入库的 `voice-launch.json`；QQ 自动启动服务，日常不必运行 `voice_dev.py run`。玩家包使用 `package_release.py --voice-runtime <Windows运行时目录>`，目录须包含 `spire-voice.exe`、依赖及 `models/vosk-model-small-cn-0.22/am/final.mdl`；源码目录和开发机的 `.venv` 不能冒充可移植运行时。未提供该参数的包继续是纯文字包，README 会明确说明。

发布含语音组件的包时同样使用 `publish_release.py --voice-runtime <Windows运行时目录>`；该参数会传给打包脚本。发布会更新 GitHub Release，仅在发布任务明确授权后执行。
