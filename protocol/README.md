# 游戏与语音通信协议

权威功能契约见 [语音模块 API](../docs/语音模块-开发功能与API.md)。C# 与 Python 共用此目录，字段变更需双方审查。

- schemas/message.schema.json：协议 v1 消息信封的 JSON Schema，当前只约束公共结构，不代表已验证每个方法的参数。
- examples/：请求、响应、事件示例，不含真实密钥。

请求使用 id、method、params；响应复用 id，使用 ok 和 result/error；事件使用 event_id、session_id、seq、name、data。普通文本和固定口令共用 input.final，避免重复执行。

新增方法时，应补充方法参数 Schema、有效与无效示例及双方契约测试。现阶段仓库检查只验证结构和示例公共字段，不能替代完整协议兼容测试。

2026-11-03：基本调试版已实现 `session.hello`、`service.status/ping`、`listening.start/stop`、`recognition.cancel`、`input.ack`、`speech.enqueue/cancel/clear/interrupt`、只读 `settings.get`。方法参数由服务校验；未实现方法返回 `method_not_supported`。完整目标仍以 API 文档为准。

三种模式由游戏端控制，不新增模型调用或协议版本：文字模式断开连接；语音→文字发送识别文本；语音→语音另将成功回复入播报队列。`state.changed.data` 基本版提供 `service`、`listening`、`playback`、`queue_length`、`current_utterance_id`、`echo_guard`。`echo_guard= mute_during_playback` 表示播报时暂停识别，按钮可打断。

协议与 C# 客户端的实际回环测试见 `voice/tests/test_voice_service.py`；CI 不访问音频设备。典型握手、识别、回执、收音与播报终态示例已放入 `examples/`。
