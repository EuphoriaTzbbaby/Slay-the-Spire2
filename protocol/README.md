# 游戏与语音通信协议

权威功能契约见 [语音模块 API](../docs/语音模块-开发功能与API.md)。C# 与 Python 共用此目录，字段变更需双方审查。

- schemas/message.schema.json：协议 v1 消息信封的 JSON Schema，当前只约束公共结构，不代表已验证每个方法的参数。
- examples/：请求、响应、事件示例，不含真实密钥。

请求使用 id、method、params；响应复用 id，使用 ok 和 result/error；事件使用 event_id、session_id、seq、name、data。普通文本和固定口令共用 input.final，避免重复执行。

新增方法时，应补充方法参数 Schema、有效与无效示例及双方契约测试。现阶段仓库检查只验证结构和示例公共字段，不能替代完整协议兼容测试。
