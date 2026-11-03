# 本地语音基本调试

Python 独立服务，使用 Vosk 中文离线识别、sounddevice 麦克风采集，以及 macOS `say` / Windows System.Speech 系统播报。游戏端仍负责 AI 请求、对话、快照和游戏动作。协议保持 v1。

这是基本联调版，供游戏负责人与语音组继续开发。Windows 是交付优先平台；本轮开发机为 macOS，Windows 音频尚未实机验收。

**当前优先级（2026-11-03）：先检测玩家发声，再验收语音→文字，AI 只回文字。** 不要求先听到 AI 声音，也不为输入调试修改系统音量。语音组要补的能力、现有代码缺口与交付清单见[语音组对接](../docs/语音模块-接口与参数分析.md)。TTS 代码保留，输出功能按后续阶段验收。

## 三种模式

游戏 F9 →「聊天」→「交流方式」：

| 模式 | 玩家输入 | AI 回复 | 麦克风与播报 |
| --- | --- | --- | --- |
| 文字 → 文字 | 打字 | 文字 | 关闭连接，停止采集与播放 |
| 语音 → 文字 | QQ 开启持续对话后说话 | 文字 | 开启语音聊天后持续收音，不自动播报 |
| 语音 → 语音（保留文字） | QQ 开启持续对话后说话 | 文字 + 系统播报 | 开启语音聊天后持续收音，成功聊天回复自动入队 |

三种模式都保留打字入口、同一个模型配置和聊天历史；切换模式不会重复向 AI 提问。语音控制口令由游戏端检查并执行，不调用大模型。`暂停`、`恢复`、`到你了`等仅完整匹配；`先别暂停`作为普通聊天。

## 首次准备

安装 Python 3.11+（建议 3.11/3.12；Windows 使用 x64），在仓库根目录运行：

```bash
python3 scripts/voice_dev.py setup
```

Windows 可将 `python3` 换成 `python`。脚本建立 `voice/.venv`、安装当前包，并从 [Vosk 官方](https://alphacephei.com/vosk/models) 下载约 42 MB 的 `vosk-model-small-cn-0.22`。依赖、模型和录音不提交 Git。macOS/Windows 的 pip 包包含 PortAudio，见 [sounddevice 安装说明](https://python-sounddevice.readthedocs.io/en/latest/installation.html)。

已有模型可用服务参数 `--model 路径` 指定。下载失败时可从官方页面手动下载、解压到 `voice/models/vosk-model-small-cn-0.22/`；该目录应含 `am/final.mdl`。

## 先检查输入，再进入游戏

```bash
python3 scripts/voice_dev.py devices
python3 scripts/voice_dev.py test-mic
```

- `devices`：列出输入输出设备。
- `test-mic`：打开麦克风，最多等 15 秒，识别一句话后退出，不保存录音。
- 指定输入设备：`python3 scripts/voice_dev.py test-mic --input-device 0`。

macOS 若弹出权限框，允许启动终端/Codex/Python 使用麦克风；拒绝后到系统设置「隐私与安全性 → 麦克风」检查。Windows 检查麦克风隐私权限及中文系统语音；缺少中文音色会返回明确错误。输出暂跟随系统默认扬声器。

macOS 的 `devices` 还会显示系统输出静音/音量。系统静音或音量为 0 时，播报返回明确错误，服务不会擅自修改系统设置。`test-mic` 超时会显示已采集采样数和峰值，帮助区分静音输入与识别问题。

`test-mic` 当前仍先加载 ASR 模型，成功标准是产生最终文字；它不是独立的实时发声/VAD 检测工具。非零峰值不等于识别到人声，超时无文字也不等于没有采到音频。语音组需先补不依赖 AI 回复与 ASR 成功的发声诊断；目前没有已接入游戏的实时电平/人声活动事件。系统输出静音不阻塞采集或语音→文字。

## 游戏输入联调（A1）

1. 更新后先重启 Python 服务（原终端 Ctrl+C，再运行 `python3 scripts/voice_dev.py run`），再重启游戏加载最新 C# Mod。旧 Python 不支持持续模式时游戏会明确报错并关闭输入。
2. 游戏窗口获得焦点后快速连续按两次 Q（间隔不超过 0.45 秒），或点「开始语音」。会打开聊天页；文字模式自动切到语音→文字，显示「持续监听中」。
3. 说一句话后停顿，确认识别文字进入游戏、AI 文字回复；再说第二句，不再按键或说唤醒词，仍应得到一次回复。
4. AI 回复期间的新语音最多排队 4 句，依次提交；排队提示不是额外的 AI 请求，满队列会提示等待。每句实际提交时读取当时可读的游戏状态。
5. 再按 QQ，或点「关闭语音」/窗口「关闭」，停止采集、取消未完成识别并清空未提交的语音。回到文字模式；已提交的问题仍可完成文字回复。关闭后再说话不应进入游戏。

**开关行为：** 输入框打字、长按 Q、Ctrl/Cmd/Alt/Shift 组合键及窗口失焦时不触发 QQ。F9 只隐藏窗口，监听继续，左上角「AI 搭子 · 听」保留状态提示。游戏重启后持续收音默认关闭，即使保留了语音模式配置，也要再次 QQ 开启。连接期间同样可以 QQ 关闭，关闭后不会自动恢复；保持开启时断线重连会恢复持续模式，但不重放旧句。

输入阶段不点「试听」、不切语音→语音，不修改系统音量。后续输出模式仍使用现有半双工：播报时临时暂停识别，结束后恢复；QQ 此时也是关闭整个语音聊天，不是单句打断键。独立 `test-mic` 仍是最多 15 秒的一句识别诊断，游戏中的持续模式静默时不会因此关闭。

服务可先于游戏启动；没有有效会话文件时拒绝鉴权，绝不回退为接受任意令牌。游戏开启语音后写入随机会话信息，服务每次握手重新读取。默认位置：

- macOS：`~/Library/Application Support/SlayTheSpire2/spire-companion/voice-session.json`
- Windows：`%APPDATA%\SlayTheSpire2\spire-companion\voice-session.json`

自定义游戏用户目录可传 `--token-file "实际路径/voice-session.json"`。服务只绑定 `127.0.0.1:17860`，一次只接一个游戏实例。默认不保存录音或识别文本日志；游戏聊天与现有游戏日志仍可包含文字。

## 当前边界

- 发声开始/结束与实时 VAD 尚未完成独立诊断及游戏事件；当前峰值统计与 `recognition.started` 均不能当作实时人声检测通过。
- **当前 QQ 使用免唤醒词的持续模式。** 旧 wake 模式仍兼容 ASR 文本开头匹配「搭子」，不等于专用唤醒模型；其准确率单独验收。
- 后续播报期间仍暂停识别并丢弃回声音频；QQ 关闭语音聊天。播报期间语音唤醒、真正的回声消除尚未实现。
- 自动播报当前覆盖成功聊天回复；主动发言、自动决策、低血提醒和安慰的播报挂点留待后续接入。
- 支持普通 FIFO、紧急插播、按条/组取消、清空、TTL、输入回执、重连与去重。断线后停止音频，不重放旧口令或旧播报。
- 音量/语速/音色选择、运行中切换设备、自定义唤醒词、完整服务打包尚未实现；不支持的方法返回明确错误。
- 2026-11-03 用户实机确认：真人语音可转成文字，并出现在游戏的「你」气泡中。当前 AI 接收这份转写文字，未接收原始音频；转写质量、实时 VAD、回复相关性与 Windows 音频分别验收。此前无有效文本的测试超时不再代表最新输入状态。

## 自动检查与模拟服务

```bash
python3 scripts/game_dev.py check
python3 scripts/voice_dev.py check
```

跨语言检查使用本机 .NET 9，先构建游戏检查程序后运行：

```bash
SPIRE_VOICE_WIRE_CHECK=1 python3 scripts/voice_dev.py check
```

PowerShell：先执行 `$env:SPIRE_VOICE_WIRE_CHECK='1'`，再运行检查。CI 在 Windows/Linux 自动运行协议与 C# 客户端检查；这些检查不访问麦克风，不代替音频实测。

只调协议时使用 `voice/.venv/bin/python voice/mock_voice_server.py`（Windows：`voice\.venv\Scripts\python.exe voice\mock_voice_server.py`）。终端输入模拟 `input.final`，播报只打印文字，没有真实声音；真实服务和 mock 不可占用同一端口。

完整目标见[语音模块 API](../docs/语音模块-开发功能与API.md)，消息示例见[protocol](../protocol/README.md)。
