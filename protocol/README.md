# 游戏与语音通信协议

权威功能契约见 [语音模块 API](../docs/语音模块-开发功能与API.md)。C# 与 Python 共用此目录，字段变更需双方审查。

当前优先对接玩家输入，AI 文字回复。语音组任务与验收见[对接清单](../docs/语音模块-接口与参数分析.md)，A1 最小兼容接口见主 API 第 4 节。当前 QQ 请求 `listening.start({mode:"continuous"})`，再按 QQ 关闭连接并停止采集。输入专用服务需要支持持续模式、响应 `listening_mode:"continuous"`，并兼容旧 `speech.interrupt` 和 `speech.clear`；不要求 TTS。

- schemas/message.schema.json：协议 v1 消息信封的 JSON Schema，当前只约束公共结构，不代表已验证每个方法的参数。
- examples/：请求、响应、事件示例，不含真实密钥。

请求使用 id、method、params；响应复用 id，使用 ok 和 result/error；事件使用 event_id、session_id、seq、name、data。普通文本和固定口令共用 input.final，避免重复执行。

新增方法时，应补充方法参数 Schema、有效与无效示例及双方契约测试。现阶段仓库检查只验证结构和示例公共字段，不能替代完整协议兼容测试。

2026-11-03：基本调试版已实现 `session.hello`、`service.status/ping`、`listening.start/stop`、`recognition.cancel`、`input.ack`、`speech.enqueue/cancel/clear/interrupt`、只读 `settings.get`。方法参数由服务校验；未实现方法返回 `method_not_supported`。完整目标仍以 API 文档为准。

三种模式由游戏端控制，不新增模型调用或协议版本：文字模式断开连接；语音→文字发送识别文本；语音→语音另将成功回复入播报队列。`state.changed.data` 基本版提供 `service`、`listening`、`playback`、`queue_length`、`current_utterance_id`、`echo_guard`、`listening_mode`。`echo_guard= mute_during_playback` 表示播报时暂停识别，按钮可打断。

协议与 C# 客户端的实际回环测试见 `voice/tests/test_voice_service.py`；CI 不访问音频设备。典型握手、识别、回执、收音与播报终态示例已放入 `examples/`。

当前没有已接好的实时电平/VAD 事件，`recognition.started` 仅表示收音会话开始。新增人声活动、部分识别等事件时同步语音实现、游戏显示与方法/事件 Schema；不能只修改本说明就宣称已有该能力。

持续模式的 `listening=continuous` 表示会话保持采集，逐句发送不同 input_id，不代表实时 VAD 阳性。默认 mode=wake 保留旧行为。新增 `schemas/listening-start.schema.json` 描述 mode 参数；持续开始、状态与停止示例在 examples/。旧服务不能静默降级成等待唤醒。

进程由游戏按需启动，日常 QQ 操作不要求玩家开终端。托管启动参数 `--port`、`--token-file`、`--model`、`--managed` 属于进程入口，不是 WebSocket 请求。`--managed` 服务在父进程的私有标准输入管道 EOF 后停止音频并退出；QQ 关闭仅断开会话、释放麦克风，后台模型可保持就绪，更新后重启游戏会回收并加载新的自启服务。正式玩家包须另附同平台可执行文件、依赖和模型。
