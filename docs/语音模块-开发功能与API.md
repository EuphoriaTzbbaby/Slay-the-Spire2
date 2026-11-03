# 语音模块开发功能与 API

更新：2026-11-03。本文是游戏组与语音组的对接依据；工作包和验收清单见[接口与参数分析](语音模块-接口与参数分析.md)，运行方式见[voice/README.md](../voice/README.md)。

## 1. 当前目标与阶段

当前优先完成**玩家语音输入**：先确认能收到玩家发声，再验证中文转文字。AI 暂时只用文字回复；扬声器静音、没有 TTS 音色或没有完成播报，不阻塞输入验收。

| 阶段 | 交付结果 | 本期要求 |
| --- | --- | --- |
| A0 发声检测 | 麦克风能采集，能观察说话开始/结束、输入电平与无声情况 | 当前第一优先；不要求 ASR 成功，也不要求播放声音 |
| A1 语音 → 文字 | 中文识别结果经 WebSocket 进入游戏聊天，AI 返回文字 | 输入链路的下一步；先用「说一句」按钮 |
| A2 唤醒输入 | 默认唤醒词「搭子」，一次唤醒收一句，误触发与超时可控 | 后续增强，不阻塞按钮输入 |
| B 语音 → 语音 | 游戏把回复交给 TTS，保留文字，支持队列、取消和打断 | 后续阶段，本轮不要求实际出声 |

文字→文字保持可独立使用。三种交互模式复用游戏端同一模型配置、聊天历史和状态快照，语音组不另建一套 AI 聊天或策略系统。

**采集到非零音频、检测到人声、识别出文字是三个不同结果。** 输入峰值非零也可能来自底噪或键盘；最终文本为空也不能据此断言麦克风没有采集。`recognition.started` 表示一次收音会话开始，不代表已检测到玩家正在说话。

## 2. 两组职责

| 工作 | 负责方 |
| --- | --- |
| 麦克风、权限、设备、采样、电平、发声检测/VAD、句末判断、ASR | 语音组 |
| 语音服务、协议响应、输入 ID、识别取消/超时、错误与资源释放 | 语音组 |
| 固定短口令的完整匹配，输出原文与可选 intent | 语音组；游戏端复核并执行 |
| 模型请求、游戏快照、聊天上下文、人格、发言内容与时机 | 游戏负责人 |
| 聊天字幕、模式选择、收音按钮、状态/错误展示、游戏动作权限 | 游戏负责人 |
| 角色策略、构筑评分、求解器算法与性能 | 策略优化同学 |
| 后续 TTS、播放队列、文本标记清理、音频打断与回声处理 | 语音组；游戏端提供文本、优先级与取消时机 |

语音端只发送识别文本或控制请求，不直接调用游戏 API，不读取存档，不维护角色策略。游戏模型的 API Key 不传给本地语音服务。后续播报清洗只移除标记、必要时切句，不另调大模型改写结论、卡牌名或数值。

语音模块交付优先 Windows，同时维护跨平台协议；游戏负责人在 macOS 联调。开发交付先提供源码、依赖、模型获取方式与启动说明，面向玩家的免 Python 安装包另阶段验收。

## 3. 连接与消息信封

- Python 是 WebSocket 服务端，绑定 `127.0.0.1:17860`；C# Mod 是客户端。修改端口必须同步两端，当前游戏端固定使用 17860。
- 游戏开启语音时生成随机 token，写入 `user://spire-companion/voice-session.json`；Python 读取同一文件，或用 `--token-file` 指定。以游戏日志 `voice_start path=...` 的实际路径为准。
- `session.hello` 验证 token 和协议版本后才允许服务开启采集。文件缺失或 token 不符必须拒绝，不能接受任意 token。
- 独立 `test-mic` 是用户主动启动的设备诊断，不走游戏握手；这不意味着后台服务可在未鉴权时采集。
- 当前服务一次只连接一个游戏实例。双开与跨实例输入分发不属于本轮输入验收。

请求：`version:1, kind:"request", id, method, params`。每个请求均须响应，包括 `input.ack`；成功使用相同 `id`、`ok:true` 与对象 `result`，失败使用 `ok:false` 与 `error:{code,message,retryable}`。

事件：`version:1, kind:"event", event_id, session_id, seq, name, data`。每个连接生成新的 `session_id`，事件 `seq` 在该会话递增。事件不复用请求 ID 作为输入去重依据。

```json
{
  "version": 1,
  "kind": "request",
  "id": "req-hello-001",
  "method": "session.hello",
  "params": {"token": "example-not-a-real-token", "protocol_version": 1}
}
```

握手结果至少含非空 `session_id`、`features` 和服务状态。`features` 只声明实际可用能力；只有输入的服务可声明 `asr`、`push_to_talk`，不必为了接游戏而实现 TTS。当前客户端尚未按能力清单禁用全部播报按钮，因此 A1 验收选择「语音→文字」，不点击「试听」。能力驱动的界面限制归游戏端后续完善。

当前 C# 单次请求超时为 10 秒，约每 5 秒发送一次心跳。模型加载不能让请求处理、心跳和停止操作长期等待。现有原型在开放端口前加载模型；若语音组改成握手返回 `loading`，须同步游戏端的就绪与恢复流程，不能假定现有客户端会收到 `service.ready` 后自动重试。

## 4. A1 最小兼容请求

以下是对接当前游戏端需要保留的接口。`speech.interrupt` 虽带 speech 前缀，也用于没有任何播报时的「说一句」按钮，不依赖 TTS。

| 方法 | 参数 | 语义 |
| --- | --- | --- |
| `session.hello` | `token`, `protocol_version:1` | 鉴权，返回会话与能力；失败不得采集 |
| `service.status` | `{}` | 查询状态，不开启麦克风 |
| `service.ping` | `{}` | 快速响应心跳，不改变收音状态 |
| `listening.start` | `{}` | 开启采集并进入 `waiting_wake`；还没有开启一次无唤醒词收音 |
| `listening.stop` | `{}` | 停止采集、取消未完成识别；不发送残留最终文本 |
| `speech.interrupt` | 可选 `group_id` | 麦克风已开启时，取消旧播放与排队内容，开始一次收音；麦克风关闭返回 `microphone_disabled` |
| `recognition.cancel` | `input_id` | 取消指定的一次输入，回到等待状态 |
| `input.ack` | `input_id`, `status`，可选 `reason` | 接收游戏路由回执，并响应 `{}` 或其他对象 |
| `speech.clear` | `include_current:bool` | 当前游戏聊天/取消路径也会发送；输入专用服务应兼容为空操作，返回 idle、空队列，不启动收音 |

输入阶段无需实现 `speech.enqueue`、音色选择或播放器。**不能把 `speech.clear` 一并删掉**，否则现有游戏端可能在正常聊天时显示不支持该方法的错误。除上述兼容方法外，未支持的方法返回 `method_not_supported`，不要假装调用成功。

基本版 `service.status` 和 `state.changed.data` 使用这些字段：

| 字段 | 基本版值/含义 |
| --- | --- |
| `service` | `ready`；完整目标另有 `loading/error/stopping`，尚未形成完整就绪流程 |
| `listening` | `disabled`、`waiting_wake`、`listening`；当前没有独立的实时 VAD/recognizing 状态反馈 |
| `playback` | `idle` / `playing`；纯输入服务固定 `idle` |
| `queue_length` | 非负整数；纯输入服务为 0 |
| `current_utterance_id` | 播放标识或 `null`；纯输入服务为 `null` |
| `echo_guard` | 现有原型为 `mute_during_playback`，只表示播报时暂停识别，不代表回声消除已完成 |

A0 的实时电平/人声活动目前没有已接通的游戏事件。可先在语音端独立诊断界面或终端展示；新增游戏事件时，两组共同定义字段、补示例和显示逻辑。本轮文档不把未实现事件写成现有协议。

## 5. 输入事件与路由

| 事件 | data | 要求 |
| --- | --- | --- |
| `recognition.started` | `input_id` | 按钮/唤醒开启一次输入，ID 一次生成；不是 VAD 阳性事件 |
| `input.final` | `input_id`, `text`, `language:"zh-CN"`, `intent` | 本次最终结果，最多一次；普通聊天 `intent:null` |
| `recognition.timeout` | `input_id`, `reason` | 本次没有有效结果或超过时限，不操作游戏 |
| `recognition.cancelled` | `input_id`, `reason` | 本次取消，不再发送旧文本 |
| `state.changed` | 第 4 节的状态 | 采集/播放变化可观测；不能把 `waiting_wake` 当成正在识别人声 |
| `service.error` | `code`, `message`, `retryable`，可选 `input_id` | 关联输入失败，显示原因；不当作聊天发送 |
| `wake.detected` | `input_id`, `wake_word` | A2 增强；按钮输入不要求先发生此事件 |

```json
{
  "version": 1,
  "kind": "event",
  "event_id": "evt-001",
  "session_id": "voice-example",
  "seq": 1,
  "name": "input.final",
  "data": {
    "input_id": "input-001",
    "text": "这手牌怎么打？",
    "language": "zh-CN",
    "intent": null
  }
}
```

基本版限制：游戏接收的 `input_id` 长度 1–128，文本最多 4000 字符，不接受空白普通文本。最终文本只在本次输入有效时发送；不要为了显示电平或部分识别而多次发送 `input.final`。需要流式部分结果时另行约定事件。

固定口令仅做完整匹配或受控归一化：

| 说法示例 | intent | 游戏端处理 |
| --- | --- | --- |
| 暂停、等一下 | `pause` | 检查并暂停自动操作/已接管求解器 |
| 恢复、继续操作 | `resume` | 检查当前是否允许恢复 |
| 到你了、你来打 | `handoff` | 检查是否处于可接管战斗 |

`先别暂停`、`暂停以后怎么办`等混合句不按关键词命中，作为普通文本。固定口令与普通文本共用一次 `input.final`，不要另外发送第二条“控制事件”。

每次输入只有一个终态：最终文本、超时、取消或关联错误。游戏端按 **`(session_id,input_id)`** 去重，并丢弃旧会话已排队回调；事件 seq 不能代替输入 ID 去重。

`input.ack.status`：`accepted` 表示已交给聊天或控制处理，`rejected` 表示未接收，`ignored` 表示重复/未知/当前不适用。**accepted 不是 AI 已答完或游戏动作已最终完成。** 原型可能对不适用口令回 ignored；原因字段目前未统一发送。回执丢失不授权重放动作，语音端不应换新 input_id 重发旧句。

断开连接时停止采集/识别/播放、清空旧输入与队列；重连后新握手、查询状态，由游戏端明确恢复。禁止重放断线前的口令和播报。

## 6. 当前按钮/QQ 快捷键输入时序

```text
选择「语音→文字」
→ session.hello → service.status → listening.start（waiting_wake）
玩家点「说一句」，或快速连续按两次 Q
→ speech.interrupt → recognition.started（开始本次收音）
→ 玩家发声、停顿 → ASR 得到最终文字
→ input.final(intent=null)
→ 游戏复用聊天入口 → input.ack(accepted/rejected)
→ AI 回复文字；不发起自动播报
```

A0 先验证发声检测，不要求走完这条 AI 链路。系统扬声器保持静音也应能完成 A0/A1。麦克风权限、输入静音和输出静音分别检查，不能用“没听见 AI”判断玩家输入失败。

QQ 的检测、打开聊天页和默认切到语音→文字均归游戏端。它与按钮复用同一 `listening.start` → `speech.interrupt` 流程，语音组不需要新增快捷键协议或在 Python 监听系统键盘。仅游戏窗口有焦点且未编辑文本时生效；初次连接的待收音请求 5 秒失效，关闭输入或焦点丢失时不执行过期请求。

## 7. 后续 B 阶段：TTS 与播放

| 方法 | 参数/规则 |
| --- | --- |
| `speech.enqueue` | `utterance_id`, `text`, `priority:normal/urgent`, `ttl_ms`，可选 `group_id` |
| `speech.cancel` | 按 `utterance_id` 或 `group_id` 取消排队/当前播放 |
| `speech.clear` | 清队列，可同时停播，不自动开始收音 |
| `speech.interrupt` | 停播、清旧内容并开启一次收音；不擅自打开关闭的麦克风 |

当前游戏发送成功聊天回复，默认 `ttl_ms=60000`，Python 播报前清理 Markdown/常见动作 ID；游戏端尚未保证传入纯文本。语音端不得二次生成或改写事实。`category/scene/dedupe_key` 只是旧稿建议扩展，目前不作为必填字段。

普通 FIFO；紧急播报打断当前句，不自动重播；TTL 从接受请求起计时。每条接受的播报必须有完成、取消或关联错误终态；入队响应不等于已经出声。

已有原型支持 `speech.started/completed/cancelled`；合成失败使用带 `utterance_id` 的 `service.error`。当前播报时暂停识别，按钮可打断；**播报期间语音唤醒与真正回声消除尚未实现**。这些是后续增强，不能用半双工方案宣称已验收完整目标。

设备运行中切换、音量/语速/音色设置、自定义唤醒词、服务退出 API 和最终打包后续补充。当前只有只读 `settings.get`；`devices.list/select`、`settings.update`、`service.shutdown` 尚未实现为服务请求，不能按旧表当作可调用能力。

## 8. 状态、错误与验收证据

| 范围 | 已有证据 | 尚缺 |
| --- | --- | --- |
| 协议/客户端 | 13 项音频无关检查，包括 C#↔Python、中文分片、鉴权失败、去重、断线清理和静音提示 | 不证明真实麦克风、VAD 或识别效果 |
| 本机设备 | 能枚举输入设备；`test-mic` 可打印识别结果或超时采样/峰值 | 实时发声反馈、设备/权限异常的完整验收 |
| 本地 ASR | 已识别合成 PCM；2026-11-03 用户确认真人转写进入游戏「你」气泡 | 转写质量、实时 VAD 与 Windows 仍待分别实测；AI 回复相关性按聊天修复后复测 |
| 唤醒 | 仅 ASR 最终文本开头匹配「搭子」 | 专门唤醒检测与误触发评估；样本里「搭子」曾识别为「大致」 |
| TTS | 基本代码存在；系统静音可读出并明确报错 | 使用者确认听感、Windows 音频和完整播报交互；本轮暂缓 |

至少区分鉴权/协议、模型、权限、设备、识别与取消/超时等问题。当前原型把部分权限/设备错误合并到 `audio_unavailable`，细分错误与真实异常恢复需要语音组补齐；新增错误值同步示例和游戏显示。

测试报告分别记录设备采集、发声检测、ASR、WebSocket、游戏文本回复。延迟也分别计量：说话开始→检测到人声，停顿结束→最终文字，游戏收到文字→AI 回复；后续才测 TTS 首音延迟。旧稿“首包 <2 秒”不是本期统一验收阈值，先记录实测与硬件，再由两组确定目标。

代码与通用示例在 [protocol/](../protocol/README.md)。现有 Schema 只校验信封，不等于所有方法参数与真人音频均已验收。默认不保存原始录音，模型、录音、令牌和个人设备配置不进入源码仓库。
