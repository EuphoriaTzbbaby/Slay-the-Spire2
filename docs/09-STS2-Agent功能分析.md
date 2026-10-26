# STS2-Agent 项目功能分析

> 分析对象：本地 `STS2-Agent/`（CharTyr/STS2-Agent）
> 分析时间：2026-10-22
> 代码版本：本地 manifest 为 `0.16.2`；`PRODUCT_PLAN_CURRENT.md` 记录的最近发布为 `v0.16.1`，以实际安装包/工坊为准
> 许可证：AGPL-3.0-only
> 关联：[AI 双排规划与难点分析](08-AI双排规划与难点分析.md)、[竞品与方案调研](07-竞品与方案调研.md)

---

## 0. 一句话结论

**它不是“LLM 聊天框”，而是一套完整的 StS2 游戏内 AI Agent 运行时：**

```text
游戏内 Overlay
  + OpenAI-compatible 模型接入
  + 自动游玩（战斗/选牌/商店/事件/路线）
  + 本地双实例 co-op（人类 + AI 队友）
  + 团队聊天/主动短评
  + 本地 HTTP API / MCP / 外部 Agent 接管
  + 预算、熔断、存档隔离、决策日志
```

它已经覆盖了“AI 双排”从**状态读取 → 决策 → 执行 → 界面 → 对话 → 开发接口**的全链路。

---

## 1. 项目定位

| 项目 | 内容 |
|---|---|
| 名称 | STS2 AI Agent |
| 作者 | CharTyr（杉茶） |
| 形态 | StS2 的 C# Mod（Godot 游戏内 UI）+ 可选 Python MCP sidecar |
| 目标 | 让 AI 自动玩 StS2，或作为人类玩家的 co-op 队友 |
| 最低游戏版本 | StS2 `v0.111.0` |
| 分发 | Steam 创意工坊（免费订阅）+ GitHub Release + 源码构建 |
| 开源协议 | AGPL-3.0-only |
| 平台 | 以 Windows 为主；仓库提供 macOS/Linux 构建脚本 |
| 依赖 | .NET 9、Godot 4.x、游戏程序集；Python 3.11+/uv 仅 sidecar 需要 |

代码规模（本地统计）：

- `STS2AIAgent/`：约 172 个 C# 源文件
- `STS2AIAgent.Tests/`：约 125 个 C# 测试文件
- `mcp_server/`：约 47 个 Python 文件

---

## 2. 功能总览

| 大类 | 能提供什么 |
|---|---|
| 游戏内 UI | F8 悬浮窗、AI 标签、主题/透明度、位置记忆、中英跟随游戏语言 |
| 模型接入 | OpenAI-compatible 端点、多端点/多模型、Chat/Play/Vision 角色、思考强度、工具调用测试 |
| 自动游玩 | 战斗、奖励选牌、商店、事件、地图、Capstone、Bundle、角色选择、时间线、解锁、Game Over |
| 双排 co-op | 本地双实例、邀请 AI 队友、自动/手动选角、继续联机存档、地图投票、队友状态、暂停/接管 |
| 对话/人格 | 团队聊天、自然语言指令、主动短评、三种语气、决策理由 |
| 开发者接口 | HTTP API、SSE 事件流、原生 MCP、Python sidecar、外部 Agent 接管 |
| 数据导出 | cards / relics / monsters / potions / events / powers / characters |
| 可靠性 | 会话预算、连续失败熔断、Run 边界、动作门禁、存档隔离、诊断导出 |
| 测试/发布 | C#/Python 契约测试、发布预检、GitHub/Workshop 双渠道、构建指纹 |

---

## 3. 游戏内 Overlay

### 3.1 入口

- 游戏内按 **F8**（可配置）
- 或点击屏幕右侧灰色 **AI** 标签
- 不依赖浏览器/外部窗口

### 3.2 界面结构

当前源码把功能收束为两个主标签：

```text
Play（游玩）
  - 单人/多人模式切换
  - 开始自动游玩 / 单步执行 / 暂停
  - 对话
  - 决策日志
  - Jev 执行层面板

Settings（设置）
  - First-time setup（首次设置）
  - Appearance（外观/主题）
  - Endpoints（端点）
  - Models（模型）
  - Role assignment（角色分配）
  - Jev execution layer（执行层）
  - Connect（MCP/外部接入）
```

> 注意：README 仍按“AI Teammate / Chat / Play / Settings / Connect / Decision Log”六页描述；当前源码已经改成 Play/Settings 两页 + 内部分区。版本之间 UI 有差异，以实际安装版本为准。

### 3.3 外观

- 多套主题（含浅色）
- 窗口位置/主题记忆
- 透明度/滚动/布局处理
- 跟随游戏语言（中文/英文）

### 3.4 已知 macOS 问题

在 macOS + 简体中文环境下，overlay 的中文会显示为 `□`：

- 英文 URL/按钮正常 → 不是编码错误
- 原因：Godot overlay 没有 CJK 字体 fallback
- 临时绕过：游戏语言切 English
- 上游问题，不是模型配置/订阅问题

---

## 4. 模型接入

### 4.1 支持形态

- OpenAI-compatible Chat Completions
- 可配置多个 Endpoint
- 每个 Endpoint 下可配置多个 Model
- 可分别指定：
  - **Chat Model**：对话
  - **Play Model**：自动游玩
  - **Vision Model**：截图理解（可选）
- 支持思考强度/推理参数（取决于服务商）
- 支持流式输出、usage 统计（取决于服务商）
- API Key 可空：Ollama / LM Studio

### 4.2 模型测试

不是简单“能聊天”：

1. 连通性测试
2. **工具调用（tool calling）探针**
3. 失败会显示 401/403/404 等错误
4. `Capability: unverified` 表示还没通过游玩能力验证

> 聊天通过 ≠ 能自动打牌。自动游玩依赖工具调用。

### 4.3 预算与成本

- 会话 Token 上限
- 请求次数上限
- 实时显示 Prompt/Completion/Total Token
- 达到上限自动停止
- 缺失 usage 显示“未知”，不当作 0
- 预算护栏不是账单金额保证

### 4.4 已知限制

- 模型必须支持 tool calling
- 不同服务商的工具调用/流式/usage 兼容性不同
- 模型响应慢会直接影响游玩节奏
- 401 通常是 Key/端点/权限问题，不是 Mod 问题

---

## 5. 自动游玩（Auto-Play）

### 5.1 覆盖的决策面

从 README、API 文档和源码看，自动游玩覆盖：

- **战斗**：出牌、目标、药水、结束回合
- **卡牌奖励**：选牌、跳过
- **商店**：买卡/遗物/药水、删牌
- **事件**：选项、对话推进
- **地图**：节点选择
- **休息点**：休息/锻造等
- **宝箱**：遗物选择
- **Capstone / Bundle**：开局/进阶选项
- **角色选择 / 时间线 / 解锁 / Game Over**
- **多人地图投票**

### 5.2 控制方式

- 开始自动游玩
- 单步执行（Step Once）
- 暂停 / 继续
- 随时接管
- 决策日志：每一步动作、理由、来源、成本

### 5.3 状态与动作

- 文本状态为主
- 可选截图（Vision）作为补充
- 动作必须来自 `available_actions`
- 动作返回 `completed` / `pending` / `failed`
- `pending` 表示还在过渡，不是失败
- `outcome_unknown` 时只做一次状态对齐，不自动重放

### 5.4 质量边界

官方状态页明确：

- 不保证整局通关
- 策略质量取决于模型和局面
- 离线测试 ≠ 完整实机通关
- 已有隔离档实机验证：自动游玩从 1 层到 3 层、45 次决策、约 62 万 token 后因预算停止
- 也有完整 co-op 短路径/接管验证

---

## 6. 本地双实例 Co-op（AI 队友）

这是它最接近“AI 双排视频”的能力。

### 6.1 玩法

- 人类在主窗口玩自己的角色
- 主菜单 → AI Teammate → Invite
- 自动拉起第二个游戏实例
- AI 控制第二个角色
- 共享同一局 co-op run

### 6.2 组队细节

- 队友默认自动选角并准备
- 可关闭自动选角，让 AI/人类手动选
- 支持继续上次本地 co-op 存档
- 大厅仍有 4 个座位，可再加入 2 个在线好友
- 队友只操作自己的角色；越界返回 403 `forbidden_actor`

### 6.3 队友状态与协调

- 队友 HP/格挡/能量/手牌数
- 队友连接/等待/暂停/停止原因
- 地图投票
- 团队聊天
- 自然语言指令（“集火右边”“走商店路线”）
- 主动短评（默认关闭，可设语气：轻松搭档/沉稳参谋/简短简报）

### 6.4 两条队友路线

| 路线 | 条件 | 行为 |
|---|---|---|
| AI 自走 | Play Model 已验证 | 队友自己决策、出牌、选牌 |
| 外部接管 | 可不配置模型 | 队友拉起并入队，但停在原地；外部 Agent 通过 API/MCP 驱动 |

外部接管入口：

- 主窗口 `POST /teammate/control` 开始/暂停
- 队友实例 `GET /state`、`POST /action`
- 队友 API 端口从主窗口 `/health` 的 `companion.api_port` 获取

### 6.5 隔离与安全

- 自动 `--force-steam off`
- 增量 `clientId`
- 独立 `settings.companion.json`
- 独立存档/配置，避免双实例互相覆盖
- 队友进程退出后 `/health` 会清理
- 本地联机存档不要用游戏自带 Load 打开（会被 Steam 联网路径判坏档），要用 Mod 的“继续上次联机对局”

### 6.6 已知限制

- 本地 co-op 是内部/测试大厅路径，属于 beta
- 控制台 `fight/die` 在双开时可能触发不同步断线
- 与其他 Mod 的适配有限；双方 Mod 不一致可能导致副窗口进不来
- macOS 双开路径未在公开文档中确认稳定

---

## 7. 对话与人格

### 7.1 对话

- 游戏内 Chat
- 可附加当前状态（牌组/遗物/HP/路线）
- 玩家自然语言提问
- 队友回复并影响后续决策
- 聊天是只读安全设计：不会替人类出牌，也不会解除暂停

### 7.2 主动发言

- 默认关闭
- 开启后只在战斗开始/结束各一句
- 语气可选
- 遵守暂停和预算
- 每会话条数上限 + 间隔限制

### 7.3 决策解释

- 每次被接受的动作可带 `reason`
- 决策日志显示动作、理由、来源、成本
- SSE `decision_made` 可实时推送
- 支持 `/decisions` 和 MCP `get_decision_log`

---

## 8. 本地 HTTP API

默认：

```text
http://127.0.0.1:8080
```

端口冲突时动态回退；以 `/health` 的 `api_port` 为准。

### 8.1 主要端点

| 方法 | 路径 | 用途 |
|---|---|---|
| GET | `/health` | Mod/游戏/会话/队友状态 |
| GET | `/state` | 完整游戏状态（含 compact agent_view） |
| GET | `/actions/available` | 当前合法动作与参数 Schema |
| POST | `/action` | 执行动作 |
| GET | `/events/stream` | SSE 事件流 |
| GET | `/decision-snapshot` | 决策快照 |
| GET | `/decisions` | 决策日志 |
| POST | `/session/control` | 开始/暂停当前实例自动游玩 |
| POST | `/teammate/control` | 主窗口开始/暂停 AI 队友 |
| GET/POST | `/strategy` | 双层策略（目标/姿态/指令） |
| GET | `/companion/jev` | 队友 Jev 执行层状态 |
| POST | `/companion/control` | 控制队友实例 |
| POST | `/companion/message` | 给队友发消息 |
| GET | `/data/{collection}` | 导出卡牌/遗物/怪物等元数据 |
| GET | `/diagnostics` | 诊断信息 |
| GET | `/vision/screenshot` | 截图（Vision） |
| POST | `/mcp/control` | 开关原生 MCP |
| POST | `/mcp` | 原生 MCP（Streamable HTTP） |

### 8.2 错误契约

- 统一 `{ok, request_id, data|error}`
- 错误码明确：`invalid_action`、`invalid_target`、`action_in_flight`、`state_unavailable`、`forbidden_actor`、`session_not_ready` 等
- 可重试/不可重试有标记
- 索引错误会带 `field` / `submitted` / `valid_indices`

### 8.3 动作面

非常完整，包含：

- 战斗：`play_card`、`end_turn`、`use_potion`、`discard_potion`
- 流程：`continue_run`、`embark`、`choose_map_node`、`proceed`、`save_and_quit`
- 奖励：`claim_reward`、`choose_reward_card`、`skip_reward_cards`、`resolve_rewards`
- 商店：`buy_card`、`buy_relic`、`buy_potion`、`remove_card_at_shop`
- 事件/休息/宝箱/水晶球
- 多人：`invite_ai_teammate`、`continue_ai_teammate`、`host/join/ready/disconnect_multiplayer_lobby`
- 选牌/确认/跳过/Bundle/Capstone
- 弹窗/解锁/时间线/看牌

---

## 9. MCP 与外部 Agent

### 9.1 原生 MCP

- 游戏内 Connect 页开启
- 同一 HTTP 端口 `/mcp`
- 支持 Cursor / Claude / Codex 等
- 工具包括：
  - `health_check`
  - `get_game_state`
  - `get_available_actions`
  - `act`
  - `get_game_data_*`
  - `wait_until_actionable`
  - `decide`
  - `get_scene_guidance`
  - `get_decision_log`
  - `get_run_summary`
  - `diff_state`

### 9.2 Python sidecar

- 可选，不是玩家必装
- 支持 stdio / layered / full profile
- `guided` 默认 17 个工具
- `layered` 增加主/副 Agent handoff 工具
- `full` 增加 legacy per-action 工具
- 适合开发、契约测试、外部编排

### 9.3 外部接管

- 无模型也能拉起队友
- 外部 Agent 通过队友实例 API 驱动
- 主窗口 `/teammate/control` 控制开始/暂停
- 已有真实外部 Agent 接管战斗的验证记录

---

## 10. 数据与状态

### 10.1 状态覆盖

- 菜单/角色选择/多人大厅/多人读档
- 地图/战斗/事件/商店/休息/宝箱/奖励
- 卡牌选择/看牌/牌堆/Bundle/Capstone
- 暂停菜单/设置/百科/图鉴/历史
- 解锁/时间线/Game Over/反馈
- 联机：玩家、投票、大厅、队友

### 10.2 元数据

`GET /data/{collection}` 支持：

```text
cards
relics
monsters
potions
events
powers
characters
```

数据来自运行中的游戏，不是包内静态快照。

### 10.3 compact agent view

- 专为 LLM token 优化
- 减少冗余字段
- 有完整状态与 compact 两种视图
- 文档说明 compact 的字段改名/移除

---

## 11. 可靠性设计

| 机制 | 作用 |
|---|---|
| SessionBudgetGuard | Token/请求上限 |
| AutoPlayRecovery | 连续失败熔断 + 指数退避 |
| CurrentRunBoundary | 绑定 runId，退出/结束即停 |
| ActionExecutionGate | 防止并发动作/重复动作 |
| GameTaskWaitPolicy | 游戏 await 有界，超时返回 pending |
| StateInvariants | 状态一致性检查 |
| ProgressSaveVerification | 存档验证 |
| CoopLaunchPolicy | 双开结构/模型门禁 |
| CompanionProfileBootstrap | 队友配置/存档隔离 |
| Diagnostics | 导出诊断，不含 Key/Token/聊天正文 |
| DecisionLog | 决策可追溯 |

---

## 12. 测试与发布

- C# 测试：约 125 个测试文件
- Python MCP 测试：约 47 个文件
- 发布预检脚本、契约测试、lint/link gate
- GitHub Release + Steam Workshop 双渠道
- 版本/构建指纹/哈希记录
- 隔离档实机验证：自动游玩、双开 co-op、外部接管、存档哈希保护

但要注意：

- “离线测试全绿”不等于“所有平台/所有 Mod 组合都稳定”
- 文档自己明确标注了未验证边界（Steam 双开路径、完整自然对局、Vision 实机等）

---

## 13. 它最适合谁

### 玩家

- 想挂机自动打
- 想和一个 AI 角色双排
- 想边玩边问策略
- 想要游戏内 UI，不想切终端

### 开发者/研究者

- 想接外部 Agent（Cursor/Claude/Codex）
- 想要一个现成的 StS2 状态/动作 API
- 想研究 LLM 在卡牌游戏中的决策
- 想基于 co-op/overlay 做二次开发

### 不适合

- 只想要“最强战斗 solver”的人（应看 CombatSolver）
- 想要完全离线、零模型成本的人（除非本地小模型）
- 对 macOS/中文 UI 稳定性要求很高的人（当前有字体/平台坑）

---

## 14. 优点与局限

### 优点

1. **全链路完整**：状态、动作、UI、对话、执行、诊断都有。
2. **真 co-op**：不是假装队友，而是第二个游戏实例 + 第二个角色。
3. **开发者友好**：HTTP API + MCP + 外部接管。
4. **可靠性意识强**：预算、熔断、Run 边界、存档隔离。
5. **可解释**：决策日志 + reason + SSE。
6. **渠道成熟**：Steam Workshop 免费订阅，安装简单。

### 局限

1. **战术上限依赖 LLM**：没有确定性战斗 solver；模型弱就容易蠢。
2. **成本/延迟**：token 消耗和响应速度是硬约束。
3. **Windows 优先**：macOS 有构建路径，但中文 overlay 已出现字体问题，双开未确认。
4. **Mod 兼容性有限**：作者明确说暂未做其他 Mod 适配；多人要求 Mod 一致。
5. **AGPL**：二次分发/修改要遵守 AGPL。
6. **游戏版本绑定**：`0.111.0+`；游戏更新可能破坏反射/API。
7. **不保证通关**：官方也把它定位为开发中项目。

---

## 15. 和 SpirePilot / CombatSolver 的关系

| 能力 | STS2-Agent | SpirePilot（我们） | CombatSolver |
|---|---|---|---|
| 游戏内 overlay | ✅ | Phase A 外部窗口 | ✅ |
| 自动游玩 | ✅ | 有限/实验 | ✅ |
| 本地双开 co-op | ✅ | ❌ | ❌ |
| 对话/人格 | ✅ | ❌ | ❌ |
| 确定性战斗 solver | ❌（LLM 驱动） | ✅ exact/beam/rollout | ✅ 强 |
| 可解释对比 | 决策日志 | ✅ exact vs beam | 路线展示 |
| 开发者 API/MCP | ✅ | 部分 | 有限 |
| 中文人格/语音 | 部分 | 规划中 | ❌ |

结论：

- **要底座/双排/overlay**：用 STS2-Agent。
- **要强战斗求解**：用 CombatSolver。
- **要可解释、可对比、可研究的轻量 copilot**：SpirePilot 仍有空间。
- 最现实的组合：**STS2-Agent 提供 co-op/overlay/API，SpirePilot 提供战术 solver 和解释**；不要从零重写双实例。

---

## 16. 关键文件索引

| 文件 | 作用 |
|---|---|
| `STS2-Agent/README.zh-CN.md` | 玩家向中文说明 |
| `STS2-Agent/PRODUCT_PLAN_CURRENT.md` | 当前状态、发布证据、验收边界 |
| `STS2-Agent/docs/api.md` | HTTP API/状态/动作完整契约 |
| `STS2-Agent/mcp_server/README.md` | MCP 工具面 |
| `STS2-Agent/STS2AIAgent/Agent/` | Agent 循环、决策、预算、会话 |
| `STS2-Agent/STS2AIAgent/Game/` | 状态读取、动作执行、游戏桥接 |
| `STS2-Agent/STS2AIAgent/Multiplayer/` | 双实例、队友、团队对话 |
| `STS2-Agent/STS2AIAgent/Server/` | HTTP/MCP/SSE |
| `STS2-Agent/STS2AIAgent/Ui/` | 游戏内 Overlay |
| `STS2-Agent/STS2AIAgent/Llm/` | OpenAI-compatible 客户端 |
| `STS2-Agent/STS2AIAgent.Tests/` | C# 测试 |
| `STS2-Agent/mcp_server/` | Python MCP sidecar |
| `STS2-Agent/skills/sts2-mcp-player/` | 外部 Agent 的 play skill |

---

## 17. 最终判断

**它能提供的核心功能：**

1. 游戏内 AI 助手/自动游玩；
2. 人类 + AI 本地双排；
3. 选牌/战斗/商店/事件/路线全流程决策；
4. 游戏内对话、主动短评、决策理由；
5. 本地 HTTP API + MCP，供外部 Agent 接管；
6. 预算/熔断/存档隔离/诊断等工程保障；
7. Steam Workshop 免费分发。

**它不能提供的：**

1. 确定性、可证明的战斗最优解；
2. 跨平台完全一致的体验（尤其 macOS 中文 UI）；
3. 零成本/零延迟；
4. 与任意 Mod 无冲突的 co-op。

这也是我们后续做“中文 AI 双排搭子”时最值得借力和最需要补强的部分。

---

## 18. 技术栈与关键实现

> 这一节回答“它用了哪些技术”，不是功能清单。结论：**核心不是某个搜索算法，而是一套 LLM Agent Runtime + 游戏集成 + 工程护栏。**

### 18.1 技术栈总表

| 层次 | 技术 | 用途 |
|---|---|---|
| 游戏运行时 | Godot 4 + C# / .NET 9 | StS2 本体；Mod 以 `net9.0` 程序集加载 |
| Mod 接入 | 游戏 `ModInitializer` / `ModEntry` | 启动时注册 HTTP、Agent、Overlay、事件服务 |
| 游戏内部访问 | 反射 + 游戏程序集引用 | 读取私有字段/调用内部方法；`ReflectedGameMembers` 做启动探针 |
| 线程模型 | Godot `SynchronizationContext` + `GameThread` | 所有游戏/节点访问都切回主线程 |
| 异步 | `async/await` + `CancellationToken` + 有界等待 | 防止游戏 await 永久卡死 |
| UI | Godot `Control` 节点 + 自定义主题 | F8 游戏内 Overlay，Play/Settings 两页 |
| HTTP | .NET `HttpListener` + 自定义 `Router` | 本地 REST API，`127.0.0.1:8080`，端口冲突回退 |
| 实时事件 | SSE（Server-Sent Events） | `/events/stream` 推送状态变化/决策 |
| 序列化 | `System.Text.Json` | 紧凑 JSON、PascalCase 契约、统一错误 envelope |
| LLM 协议 | OpenAI-compatible `/v1/chat/completions` | 接 DeepSeek/OpenAI/中转/Ollama/LM Studio |
| 流式 | SSE 解析 + `SseCompletionAccumulator` | 增量内容、reasoning、tool_calls |
| 工具调用 | OpenAI `tools` / `tool_calls` | Agent 执行游戏动作的核心通道 |
| 多模型 | Chat / Play / Vision 角色 + 多 Endpoint | 对话、游玩、截图理解分开配置 |
| 视觉 | Godot 截图 + JPEG data URL | 可选 Vision 上下文 |
| 双实例 | `Process.Start` + 游戏本地 co-op 大厅 | 人类一个角色，AI 一个角色 |
| 进程隔离 | `--force-steam off`、独立 `clientId`、独立 settings/profile | 防止双开串存档/配置 |
| 进程间通信 | 本地 HTTP + `X-STS2-Companion-Session` token | 主窗口控制队友实例 |
| MCP（原生） | JSON-RPC / Streamable HTTP | `/mcp`，给 Cursor/Claude/Codex |
| MCP（Python） | FastMCP + Pydantic + uv | 可选 sidecar，stdio/HTTP、tool profile |
| 测试 | 自定义 C# 测试 Runner + Roslyn 源码契约 | 离线测试、源码规则检查、无游戏依赖 |
| Python 测试 | unittest + FastMCP 契约 | MCP 工具面/错误信封/重放安全 |
| 构建 | PowerShell / Bash + Godot PCK 打包 | Windows 主路径，macOS/Linux 脚本 |
| 分发 | GitHub Actions + Steam Workshop | 版本、哈希、构建指纹、发布预检 |

### 18.2 Agent 运行时

核心是 `AgentRuntime` + `AgentLoop`：

```text
AutoPlaySession
  -> AutoPlayRecovery（重试/退避/无进展）
  -> turn gate（互斥）
  -> 读状态（GameThread）
  -> 组装 prompt
  -> LLM / Jev
  -> 解析 tool call / JSON
  -> 校验合法动作
  -> GameActionService
  -> 提交 receipt / 预算 / 决策日志
```

关键设计：

1. **单动作面**
   - 所有路径（游戏内 Agent、HTTP、MCP、外部 Agent）最终都走 `GameActionService.ExecuteAsync`。
   - `ActionExecutionGate` 保证同一时刻只有一个动作，避免并发点错。

2. **有界等待**
   - 游戏侧 `await` 全部有 deadline。
   - 超时返回 `pending`，不是无限挂起。
   - 有 Roslyn 源码契约测试阻止新代码写裸 `await`。

3. **状态机/策略分层**
   - `AgentRuntime` 管生命周期、预算、暂停、联机、持久化。
   - `AgentLoop` 只管“一回合决策”。
   - `AutoPlayRecovery` 管失败/无进展/退避。
   - `CurrentRunBoundary` 绑定 runId，离开对局立即停。

4. **上下文工程**
   - 稳定 prompt 前缀在前，实时状态在后（prompt cache 友好）。
   - `ContextCompaction`：上下文用到 80% 后做摘要 + 最近 ≤6 条决策。
   - `PlanningSummary` 对状态做 JSON-aware 裁剪，保证仍是合法 JSON。
   - `agent_view` 是给 LLM 的 compact 状态，完整状态另走 `/state`。

5. **工具调用循环**
   - `CompleteWithToolsAsync` 有最大工具轮数（8）。
   - 工具参数会做纯函数解析/校验。
   - 非法索引返回 409 + 合法索引，不重发同一个错误。
   - 模型不支持 tools 时有 JSON act fallback。

6. **双层决策（Jev）**
   - 一个较慢的 Planner LLM 产宏观目标/策略；
   - 一个快速执行层 Jev 在**枚举好的合法选项**里选一个；
   - 置信度低/失败时回退到主 LLM。
   - 这不是“让 LLM 自由发挥”，而是把动作空间收窄后再选。

### 18.3 LLM 客户端

`OpenAiCompatibleClient` 处理了大量 provider 差异：

- SSE 流式 vs 非流式
- `tools` / `tool_calls`
- DeepSeek/Kimi 的 `reasoning_content`
- `max_tokens` vs `max_completion_tokens`
- thinking/reasoning_effort
- 空 Key（Ollama/LM Studio）
- 非标准 tool_call 形状
- 4xx 错误分类
- usage 缺失
- 超时/停流

这层是“能接很多模型”的关键，也是为什么它能接 OpenAI-compatible 中转。

### 18.4 游戏状态与动作

- `GameStateService`：按屏幕拆分（Combat/Map/Rewards/Shop/Menus/...）。
- `GameActionService`：按领域拆 partial（Combat/Rewards/Shop/Coop/Run/...）。
- `ReflectedGameMembers`：集中管理反射成员，启动时 probe，游戏更新后能快速发现改名。
- 状态同时有：
  - 原始 `/state`
  - compact `agent_view`
  - 场景化 `game_data`
- 动作有：
  - `available_actions` 枚举
  - 索引校验
  - `requires_index` / `requires_target`
  - `pending` / `completed` / `failed` / `outcome_unknown`

### 18.5 双实例 Co-op

不是“一个进程里模拟两个角色”，而是：

```text
主进程（human）
  -> 启动第二个游戏进程（companion）
  -> 独立 settings/profile/clientId
  -> 加入本地 multiplayer lobby
  -> 主窗口通过 HTTP + token 控制队友
```

技术点：

- `LocalDualInstanceLauncher`
- `DualInstanceCoordinator`
- `CoopLaunchPolicy`
- `CompanionProfileBootstrap`
- `CompanionConnection`（token header）
- `CompanionHostWatch`（主进程死了，队友也退出）
- `CoopSavePrecheckPolicy`（防止读档把存档判坏）

### 18.6 HTTP / MCP / SSE

- `HttpListener` 监听 loopback；端口被占用时递增。
- 统一 envelope：`{ok, request_id, data|error}`。
- `Router` 一个 try/catch/finally 包住所有路由，坏请求不会杀监听器。
- SSE 通过轮询状态 + diff 产生 typed events，订阅者断开自动清理。
- 原生 MCP 是 Streamable HTTP + JSON-RPC，和 HTTP API 同端口 `/mcp`。
- Python sidecar 用 FastMCP，把同一 HTTP API 包成 stdio/network MCP。

### 18.7 可靠性工程

这是它比一般“LLM 接游戏”项目强的地方：

- `SessionBudgetGuard`：token/请求上限
- `AutoPlayRecovery`：连续失败熔断 + 2s/4s 退避
- `CurrentRunBoundary`：runId 变化即停
- `ActionExecutionGate`：单动作租约
- `GameTaskWaitPolicy`：有界 await
- `ProgressSaveVerification`：存档校验
- `DiagnosticExport`：导出诊断并脱敏
- `DecisionLog`：决策、理由、来源、成本
- `Redact`：API Key/Bearer/敏感字段脱敏
- 测试：源码契约（Roslyn）、离线 C#、Python 契约、隔离实机

### 18.8 它没有用什么

- **没有训练自己的模型**：它是 LLM 客户端/编排器，不是训练框架。
- **没有确定性战斗 solver**：战斗决策靠 LLM/Jev，不是 exact/beam/rollout。
- **没有 Harmony patch 逻辑**（虽然项目引用了 `0Harmony`，实际主要走游戏 Mod API + 反射；至少本次检索未发现成体系的 patch）。
- **没有用 BFS/DFS 求战斗最优**。
- **没有跨平台统一的 UI 字体方案**：macOS 中文 tofu 就是证据。

### 18.9 技术评价

优点：

- 工程完整度高，尤其是“单动作面 + 有界等待 + 预算 + 存档隔离”。
- 对 provider 差异处理细，能接很多 OpenAI-compatible 端点。
- 双实例 co-op 和 MCP 外部接管是稀缺能力。
- 测试意识强，源码契约测试很少见。

代价：

- 复杂度高：Agent/UI/Game/Server/Multiplayer 五个大模块。
- 反射依赖游戏内部 API，版本更新容易坏。
- LLM 延迟/成本/工具调用兼容性是产品天花板。
- Windows 优先，macOS 需要额外适配。

一句话：

> **它的技术核心是“把 LLM 安全地接进一个复杂的 Godot 游戏”，而不是“做一个比人类更强的 StS2 AI”。**
