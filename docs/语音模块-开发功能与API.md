# 语音模块开发功能与 API

更新：2026-11-09。本文是游戏组与语音组的对接依据；工作包和验收清单见[接口与参数分析](语音模块-接口与参数分析.md)，运行方式见[voice/README.md](../voice/README.md)。

## 1. 当前目标与阶段

当前基础输入与系统 TTS 已接通：负责人于 2026-11-08 确认游戏中的 AI 能说话。QQ 从关闭状态开启时默认“语音→语音（保留文字）”，重启仍默认关闭；本期完善 ASR/切句、半双工播报稳定性、异常恢复与 Windows 交付。输入专项手动选择“语音→文字”，系统输出问题不阻塞输入验收。任务和真人测试步骤以[对接任务与验收](语音模块-接口与参数分析.md)为准。

| 阶段 | 交付结果 | 本期要求 |
| --- | --- | --- |
| A0 发声检测 | 麦克风能采集，能观察说话开始/结束、输入电平与无声情况 | 已有基础，继续专项测噪声/设备；独立电平诊断仍待完善 |
| A1 语音 → 文字 | 中文识别结果经 WebSocket 进入游戏聊天，AI 返回文字 | 输入专项先选语音→文字，QQ 开启持续模式，再按 QQ 关闭 |
| A2 唤醒输入 | 默认唤醒词「搭子」，一次唤醒收一句，误触发与超时可控 | 后续增强，不阻塞按钮输入 |
| B 语音 → 语音 | 游戏把回复交给 TTS，保留文字，支持队列、取消和打断 | 基础系统播报已可闻；半双工，播报期间不接收玩家插话 |

文字→文字保持可独立使用。三种交互模式复用游戏端同一模型配置、聊天历史和状态快照，语音组不另建一套 AI 聊天或策略系统。

**采集到非零音频、检测到人声、识别出文字是三个不同结果。** 输入峰值非零也可能来自底噪或键盘；最终文本为空也不能据此断言麦克风没有采集。声明 `speech_activity` 的持续模式已由 WebRTC VAD 触发 `recognition.started`；旧 wake/单句兼容路径的 started 只表示收音会话，不能据此宣称真人检测通过。

## 2. 两组职责

| 工作 | 负责方 |
| --- | --- |
| 麦克风、权限、设备、采样、电平、发声检测/VAD、句末判断、ASR | 语音组 |
| 语音服务、协议响应、输入 ID、识别取消/超时、错误与资源释放 | 语音组 |
| 固定短口令的完整匹配，输出原文与可选 intent | 语音组；游戏端复核并执行 |
| 模型请求、游戏快照、聊天上下文、人格、发言内容与时机 | 游戏负责人 |
| 聊天字幕、模式选择、收音按钮、状态/错误展示、游戏动作权限 | 游戏负责人 |
| 角色策略、构筑评分、求解器算法与性能 | 策略优化同学 |
| TTS、播放队列、文本标记清理、音频打断与回声处理 | 语音组；游戏端提供文本、优先级与取消时机 |

语音端只发送识别文本或控制请求，不直接调用游戏 API，不读取存档，不维护角色策略。游戏模型的 API Key 不传给本地语音服务。播报清洗只移除标记、必要时切句，不另调大模型改写结论、卡牌名或数值。

语音模块交付优先 Windows，同时维护跨平台协议；游戏负责人在 macOS 联调。开发交付先提供源码、依赖、模型获取方式与启动说明，面向玩家的 Windows 独立运行时/免 Python 安装包按当前 V5 工作包单独交付验收。

玩家操作由 QQ 统一控制，不要求启动终端：游戏按需启动语音服务，关闭聊天停止收音并保留模型，退出游戏回收自启进程。开发安装由 `voice-launch.json` 连接本机环境；正式交付提供独立 `spire-voice.exe`、依赖与模型目录，打包器通过 `--voice-runtime` 纳入。此启动方式不改变 WebSocket 协议；`--managed` 表示服务须在游戏持有的标准输入管道 EOF 时清理并退出，不能在未鉴权、未收到 `listening.start` 时自动收音。自有进程崩溃最多启动 3 次；外部手动服务仅复用，不结束。

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

握手结果至少含非空 `session_id`、`features` 和服务状态。持续模式声明 `continuous_listening` 能力；输入专用服务不必实现 TTS。当前 QQ 会发送 `listening.start({mode:"continuous"})` 并核验响应 `listening_mode:"continuous"`，旧服务忽略 mode 时不能算成功，游戏会提示更新语音组件并关闭输入。`mode` 缺省仍为 `wake`，兼容旧单句客户端。

当前 C# 单次请求超时为 10 秒，约每 5 秒发送一次心跳。模型加载不能让请求处理、心跳和停止操作长期等待。现有原型在开放端口前加载模型；若语音组改成握手返回 `loading`，须同步游戏端的就绪与恢复流程，不能假定现有客户端会收到 `service.ready` 后自动重试。

## 4. A1 最小兼容请求

以下是对接游戏端的兼容接口。当前 QQ 使用持续模式启停；`speech.interrupt` 保留给旧单句客户端及后续独立打断操作，不依赖 TTS。

| 方法 | 参数 | 语义 |
| --- | --- | --- |
| `session.hello` | `token`, `protocol_version:1` | 鉴权，返回会话与能力；失败不得采集 |
| `service.status` | `{}` | 查询状态，不开启麦克风 |
| `service.ping` | `{}` | 快速响应心跳，不改变收音状态 |
| `listening.start` | 可选 `mode:wake/continuous`，缺省 wake | continuous 持续接收每句，不要求唤醒词；wake 保留等待唤醒的兼容行为 |
| `listening.stop` | `{}` | 停止采集、取消未完成识别；不发送残留最终文本 |
| `speech.interrupt` | 可选 `group_id` | 麦克风已开启时，取消旧播放与排队内容，开始一次收音；麦克风关闭返回 `microphone_disabled` |
| `recognition.cancel` | `input_id` | 取消指定的一次输入，回到等待状态 |
| `input.ack` | `input_id`, `status`，可选 `reason` | 接收游戏路由回执，并响应 `{}` 或其他对象 |
| `speech.clear` | `include_current:bool` | 当前游戏聊天/取消路径也会发送；输入专用服务应兼容为空操作，返回 idle、空队列，不启动收音 |

独立 ASR 测试服务可以不实现 `speech.enqueue`，但游戏中必须手动选择“语音→文字”；当前完整服务与 QQ 默认模式需要基础 TTS。音色选择尚未实现。**不能把 `speech.clear` 一并删掉**，否则现有游戏端可能在正常聊天时显示不支持该方法的错误。除上述兼容方法外，未支持的方法返回 `method_not_supported`，不要假装调用成功。

基本版 `service.status` 和 `state.changed.data` 使用这些字段：

| 字段 | 基本版值/含义 |
| --- | --- |
| `service` | `ready`；完整目标另有 `loading/error/stopping`，尚未形成完整就绪流程 |
| `listening` | `disabled`、`waiting_wake`、`listening`、`continuous`；continuous 表示持续收音，不代表此刻检测到人声 |
| `listening_mode` | 关闭时为 `null`，开启时为 `wake` 或 `continuous`；开始持续模式的响应必须明确返回 continuous |
| `playback` | `idle` / `playing`；纯输入服务固定 `idle` |
| `queue_length` | 非负整数；纯输入服务为 0 |
| `current_utterance_id` | 播放标识或 `null`；纯输入服务为 `null` |
| `echo_guard` | 现有原型为 `mute_during_playback`，只表示播报时暂停识别，不代表回声消除已完成 |

持续模式已接通人声活动与临时转写；握手声明 `speech_activity`、`partial_transcript`。实时电平仪仍未提供。图标的持续监听颜色不跟随 VAD，聊天输入框上方显示本句活动与临时文本。

## 5. 输入事件与路由

| 事件 | data | 要求 |
| --- | --- | --- |
| `recognition.started` | `input_id` | 每句一个 ID；声明 speech_activity 的持续模式由 VAD 触发；旧单句兼容行为除外 |
| `recognition.partial` | `input_id`, `text` | 可多次更新；text 为 1–4000 字符；仅显示草稿和触发打断，不提交 AI、不执行口令、不发 input.ack |
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

每句输入只有一个终态：最终文本、超时、取消或关联错误；一个持续监听会话可以包含多句，各句 input_id 不同。静默不会关闭持续模式或生成空输入。游戏端按 **`(session_id,input_id)`** 去重，并丢弃旧会话已排队回调；事件 seq 不能代替输入 ID 去重。

`input.ack.status`：`accepted` 表示已交给聊天或控制处理，`rejected` 表示未接收，`ignored` 表示重复/未知/当前不适用。**accepted 不是 AI 已答完或游戏动作已最终完成。** 原型可能对不适用口令回 ignored；原因字段目前未统一发送。回执丢失不授权重放动作，语音端不应换新 input_id 重发旧句。

断开连接时停止采集/识别/播放、清空旧输入与队列；重连后新握手、查询状态，由游戏端明确恢复。禁止重放断线前的口令和播报。

## 6. QQ 持续语音聊天时序

```text
游戏启动 → 可连接语音服务，但不自动采集
QQ /「开始语音」→ 需要时切到语音→文字
→ session.hello → service.status → listening.start({mode:"continuous"})
→ VAD 开始 → recognition.started → recognition.partial 临时转写
→ 句末持续非人声约 0.9 秒 → ASR 最终文字 → 每句唯一的 input.final
→ 游戏聊天 / 正在回复时取消旧请求并合并原问题与补充 → input.ack(accepted/rejected)
→ AI 文字回复，继续收音，不回到 waiting_wake
再次 QQ /「关闭语音」/ 窗口「关闭」
→ 游戏先关闭输入路由、清掉未提交语音并断开连接
→ Python 取消识别、停止麦克风、释放音频；不提交残留文本
```

`listening.stop` 也可用于协议级停止并保留连接。当前快速关闭采用断开连接，服务必须完成同样的音频清理。重连只有游戏仍处于开启意图时才明确恢复；关闭后不恢复、不重放旧句。已提交的 AI 问题可继续完成文字回复。

QQ 开关在 C# 实现；文本输入、组合键、长按与失焦不触发。F9 隐藏面板时监听继续，常驻麦克风图标仍可关闭；窗口「关闭」同时关闭持续输入。游戏重启不恢复持续收音。旧单句 QQ 的 5 秒待请求有效期不再用于持续开关；连接中可随时再次 QQ 关闭。

切句使用 WebRTC VAD：20 ms 音频帧、约 120 ms 人声确认、300 ms 前置缓存、0.9 秒连续非人声收尾、20 秒长句上限。短停顿和 ASR 提前返回的小段结果合并，最终文字只发一次。输入活动开始后另有 30 秒协议兜底；尚未开始说话的持续会话不因静默结束。阈值不是端到端延迟保证，需实测设备、噪声和解码耗时。

用户补充的有效 partial 到达时，游戏取消尚未显示的旧回复，并保留原问题；final 到达后合并请求，不把每段 partial 当问题。取消期间暂存最多 4 句，过量明确拒绝。已显示回复保留历史；未显示旧回复不得晚到覆盖新轮次。关闭、断线清空草稿、未提交补充及被打断的问题。纯噪声最终无文字时不新建问题；若已有有效 partial 导致取消旧回复，则恢复原问题。系统输出静音不影响这些输入流程。

## 7. 当前基础 TTS 与播放

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
| 协议/客户端 | 音频无关回归，包括持续多句、静默、停止/重连、旧服务检测、C#↔Python、中文分片、去重和断线清理 | 不证明真实麦克风、VAD 或识别效果 |
| 本机设备 | 能枚举设备；test-mic 显示 VAD 开始、临时/最终文字和超时采样/峰值；macOS 独立授权与退出链路已验证。2026-11-04 用户确认重启游戏后 QQ 输入基础流程成功 | 真实设备的 VAD 误报、反复开关、切句边界与异常恢复 |
| 本地 ASR / 对话 | 2026-11-03 用户确认真人转写；2026-11-04 确认“说一句话，AI 文字回复”基础链路可用，游戏日志有不同 input_id 的输入 | 短停顿/长句、补充打断、噪声、定量质量、游戏术语与 Windows 由语音组继续完善和实测 |
| 唤醒 | 仅 ASR 最终文本开头匹配「搭子」 | 专门唤醒检测与误触发评估；样本里「搭子」曾识别为「大致」 |
| TTS | 2026-11-08 使用者确认系统测试音及游戏 AI 回复可闻；已接成功聊天回复播报 | Windows 音频、不同输出设备、定量延迟、播报后恢复与真实回声；完整双工尚未实现 |

至少区分鉴权/协议、模型、权限、设备、识别与取消/超时等问题。当前原型把部分权限/设备错误合并到 `audio_unavailable`，细分错误与真实异常恢复需要语音组补齐；新增错误值同步示例和游戏显示。

2026-11-04 的输入基础验收及 11-08 的基础语音回复确认是已有证据。其余专项由语音组按当前任务表执行，负责人仅重点复测；不将代码、协议检查或一次可闻测试等同于整套语音体验验收。

测试报告分别记录设备采集、发声检测、ASR、WebSocket、游戏文本回复。延迟也分别计量：说话开始→检测到人声，停顿结束→最终文字，游戏收到文字→AI 回复；现在同时记录 TTS 实际首音和播报后恢复收音延迟。旧稿“首包 <2 秒”不是本期统一验收阈值，先记录实测与硬件，再由两组确定目标。

代码与通用示例在 [protocol/](../protocol/README.md)。现有 Schema 校验信封及部分事件字段，不等于所有方法参数与真人音频均已验收。默认不保存原始录音，模型、录音、令牌和个人设备配置不进入源码仓库。
