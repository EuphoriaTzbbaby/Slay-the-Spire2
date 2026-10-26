> 历史原型文档：以下命令均从 `game/legacy/` 执行。当前产品入口见 [根 README](../../README.md)。历史文档链接见根目录 docs/。

# SpirePilot（尖塔领航员，暂定名）

> **新方向（2026-10-22）**：从单机战斗建议扩展到 **AI 双排搭子**。产品规划、可行性、难点矩阵见 [AI 双排规划与难点分析](../../docs/08-AI双排规划与难点分析.md)。本文档其余部分描述当前已落地的单机 copilot。

> 面向《杀戮尖塔 2》（Slay the Spire 2, Early Access）的战斗出牌求解与可解释选牌推荐系统。

**一句话定位**：用「战斗前向模型 + 限定预算搜索」解决单场战斗内的出牌顺序，用「上下文特征 + 专家先验 + 证据约束生成」解决选牌推荐与原因说明；默认做人机协同的 copilot，自动执行只作为可选闭环。

| 项目项 | 内容 |
|---|---|
| 文档版本 | v0.4（2026-10-22 09:46） |
| 当前阶段 | 战斗建议 MVP + STS2MCP 真实接入 + exact/beam/rollout + Phase A overlay；AI 双排方向见 docs/08 |
| 目标对象 | 《杀戮尖塔 2》EA 桌面版，默认 Ironclad 单角色、A0–A10 |
| 核心范围 | 单场战斗内的出牌与目标选择；战斗后的三选一卡牌奖励（含跳过） |
| 当前 MVP 不做 | 读屏/读内存、地图路径规划、商店/事件/营火全自动、排行榜与成就、全角色全卡池 |
| 暂定周期 | 12 周（单人，约 12–15 小时/周），含 1 周缓冲 |

> **术语约定**：本文的「选牌」指战斗后的卡牌奖励三选一 + 跳过；战斗内「这一回合出哪张牌」归入战斗 solver。若你说的「选牌」是后者，请把它看作第一个模块的一部分。

## 现在能做什么

已经可以跑通「JSON 战斗状态 → 解析 → 合法动作枚举 → 前向模型 → beam 搜索 → 推荐动作 + 理由 + 备选」：

- 离线：`spirepilot advise --state <state.json>`，对一条真实/手工状态给建议。
- 本地 bridge：`spirepilot serve`，接收 `Communication_Mod_STS2` 的 `POST /update_state`，在终端打印建议；默认 copilot（只建议，回 `OK`）。
- 自动战斗：`spirepilot serve --auto`，把推荐动作回传给游戏；默认关闭。
- 卡牌库：87 张 Ironclad 卡（来自 Spire Codex 公开 API 的机械字段），未知卡用游戏内 `CardCatalog` 回退。
- 两个状态样本：`combat_basic.json`（字段较全）与 `combat_minimal.json`（模拟真实上游缺 `Block`/牌堆字段的形态）。
- 真实接入工具：`capture` 抓原始 JSON、`inspect` 审计字段缺口、`trace` 对拍 golden trace。
- 测试：54 个单元/集成测试，覆盖解析、规则、特殊卡牌、beam/exact 搜索、2 回合 rollout、CLI、HTTP bridge、状态审计、capture、golden trace、STS2MCP adapter 与 overlay view。

## 快速开始

```bash
# 1. 检查环境、卡牌库、示例状态
PYTHONPATH=src python3 -m spirepilot doctor

# 2. 跑内置示例
PYTHONPATH=src python3 -m spirepilot demo

# 2.5 从正在运行的游戏（STS2MCP，15526）实时给战斗建议（默认 exact）
PYTHONPATH=src python3 -m spirepilot live

# 2.6 对比 exact 与 beam
PYTHONPATH=src python3 -m spirepilot live --solver compare

# 2.7 打开置顶建议面板（Phase A）
PYTHONPATH=src python3 -m spirepilot overlay

# 2.8 对比 2 回合 rollout
PYTHONPATH=src python3 -m spirepilot rollout --turns 2

# 3. 对一条状态给 JSON 建议
PYTHONPATH=src python3 -m spirepilot advise \
  --state data/samples/combat_basic.json --json

# 4. 启动本地 bridge（默认 copilot，只建议）
PYTHONPATH=src python3 -m spirepilot serve --port 5000

# 5. 可选：自动回传动作（确认风险后再开）
PYTHONPATH=src python3 -m spirepilot serve --port 5000 --auto

# 6. 检查真实状态字段（有抓包后）
PYTHONPATH=src python3 -m spirepilot inspect \
  --capture data/replays/d1/states.jsonl --index -1

# 7. 抓真实状态（D1–D3；只记录，不执行）
PYTHONPATH=src python3 -m spirepilot capture --port 5000 --session d1

# 8. 回放 golden trace
PYTHONPATH=src python3 -m spirepilot trace \
  --trace data/traces/basic_thunderclap.json

# 9. 跑测试
PYTHONPATH=src python3 -m unittest discover -s tests -v
```

也可以直接用 `Makefile`：`make doctor`、`make demo`、`make test`、`make serve`、`make live`、`make overlay`、`make rollout`、`make inspect`、`make capture`、`make trace`。

接入真实游戏时：

1. 安装 BaseLib 与 `Communication_Mod_STS2`，确认游戏内 mod 已启用。
2. **不要同时运行上游的 `server.py`**，否则 5000 端口冲突。
3. 启动 `spirepilot serve`，再进入战斗；终端会打印推荐、理由、备选与近似警告。
4. 默认 copilot 模式只返回 `OK`，不执行动作；确认建议合理后再考虑 `--auto`。

## 当前“模型”是什么

当前**没有训练任何机器学习模型**。这里的“模型”指：

1. 手写的战斗前向模拟器（伤害/格挡/抽牌/能量/部分能力）；
2. 手调的评分函数；
3. beam search + 抽牌顺序采样；`live` 默认使用当前回合穷举 DFS（`--solver exact`）。

还不是监督学习、强化学习或神经网络。后续可以用专家轨迹做排序学习，或用 CEM 调评分权重，但当前版本是规则 + 搜索。

## 战斗建议 MVP 的能力边界

- **已覆盖**：直接伤害、格挡、抽牌、能量、易伤/虚弱、力量、部分 Ironclad 能力（Corruption、Barricade、Feel No Pain、Dark Embrace、Demon Form、Juggernaut、Rupture 等）、多目标、敌方可见攻击意图、抽牌顺序采样。
- **未完整覆盖**：遗物（除状态里已折算的力量外）、药水、部分复杂卡牌/能力、强化牌数值、敌人非攻击意图、回合外长程规划。
- **已知字段缺口**：当前上游导出没有稳定的玩家/敌人 `Block`、手牌费用/可打出标志；bridge 会对缺失字段给出警告，并按 0/基础值估算。真实接入前应先在 D1–D3 验证，必要时本地扩展 scanner。
- **不是最优解**：这是限定预算的近似搜索，不是理论最优；默认 650ms/步、2 个抽牌样本，可在 CLI 调 `--depth/--width/--budget-ms/--samples`。

## 文档地图

1. **[开题分析](../../docs/01-开题分析.md)**：选题背景、研究现状、研究问题、形式化定义、技术路线、创新点、评估方案、风险与合规。
2. **[项目规划](../../docs/02-项目规划.md)**：范围与优先级、系统架构、模块接口、数据与知识库、12 周里程碑、测试与验收、资源预算。
3. **[战斗建议 MVP 说明](../../docs/03-战斗建议MVP说明.md)**：代码结构、数据流、搜索/评估、已验证内容与已知限制。
4. **[真实接入与字段补全](../../docs/04-真实接入与字段补全.md)**：D1–D3 安装、抓状态、字段审计、本地补丁与 golden trace。
5. **[真实游戏接入操作清单](../../docs/05-真实游戏接入操作清单.md)**：可勾选的安装、capture、inspect、汇报步骤；完成一项勾一项。
6. **[开发问题与解决方案](../../docs/06-开发问题与解决方案.md)**：按时间记录环境、字段、规则、搜索、文档问题及修复。
7. **[竞品与方案调研](../../docs/07-竞品与方案调研.md)**：CombatSolver、BoberInSpire、card-advisor 等做法与我们的取舍。
8. **[AI 双排规划与难点分析](../../docs/08-AI双排规划与难点分析.md)**：从“战斗建议”扩展到“AI 双排队友”的产品规划、架构与难点。
9. **[STS2-Agent 功能分析](../../docs/09-STS2-Agent功能分析.md)**：对本地 `STS2-Agent/` 的功能、架构、API、优缺点逐项分析。
10. **[AI 双排项目执行规划](../../docs/10-AI双排项目执行规划.md)**：基于 STS2-Agent 的分阶段执行计划、工期、验收与风险。
11. **[开题报告：AI 双排助手](../../docs/11-开题报告-AI双排助手.md)**：按 PPT 页组织的游戏介绍、需求、前人工作、架构、评估与分工。
12. **[语音与游戏模块拆解](../../docs/13-语音与游戏模块拆解.md)**：语音工作包、游戏模块 G-01–G-12、依赖顺序与本周任务。
13. **[Sprint 0 任务清单](../../docs/14-Sprint0任务清单.md)**：A 观战搭子的 7 天排期、五人分工与验收。
14. **[开发日志](../../docs/LOG.md)**：按日期/时间记录已完成事项、关键证据、决策（ADR）、阻塞与下一步。

## 先给结论

### 这件事属于什么

| 模块 | 本质 | 学术叫法 | 是不是 agent |
|---|---|---|---|
| 战斗内最优出牌顺序 | 给定可见状态，在隐藏抽牌顺序与随机性下搜一条动作序列 | 部分可观测序贯决策（POMDP）/ 单人随机博弈 / 限定预算求解器 | 单独看：不是，是决策器或 solver |
| 选牌推荐 + 原因 | 对「三选一 + 跳过」做上下文排序并解释 | 上下文感知推荐 / learning-to-rank / 可解释 AI | 单独看：不是，是推荐器 |
| 读状态 → 决策 → 执行 → 看反馈 → 再决策 | 闭环自主行动 | 模型型效用 agent（model-based utility agent） | 是，闭环后才算 |
| LLM 参与决策并调用工具 | LLM 作为策略的一部分 | LLM agent | 取决于 LLM 是否真正掌决策权 |

**本项目的定位**：主贡献是「可解释的混合式决策系统」。战斗 solver 是算法决策器，选牌模块是推荐器；默认由人点击执行，因此整体是 **copilot / 决策支持系统**，不是严格意义上的 agent。只有打开自动执行并形成「状态—动作—反馈」闭环时，才把它称为 agent。LLM 在系统里主要负责解释和长程先验，不负责战术搜索的最终裁决，所以它也不是「LLM agent」。

### 难度高吗

| 工作包 | 难度 | 判断 |
|---|---|---|
| 环境接入（StS2 + Communication_Mod_STS2） | 中低 | 有现成 HTTP JSON 通道，但 EA 补丁频繁、卡牌数据字段不全、奖励选择未开放命令 |
| 战斗前向模型（卡牌/遗物/状态/随机） | 高 | 截至调研，未找到成熟、可公开复用的 StS2 战斗模拟器，必须自己实现并逐条对拍 |
| 战斗内求解 | 中高 | 单回合已可穷举 DFS；整场战斗与隐藏抽牌仍是难点 |
| 选牌推荐 | 中高 | 目标函数随卡组/遗物/幕数变化，专家标签稀缺 |
| 原因解释 | 中 | 生成容易，**忠实**很难；必须用证据约束和数值校验 |
| 自动打完整局 | 高 | 地图、商店、事件、营火、Boss 预判是另一个量级 |

**总判断**：作为 12 周单人项目，做「单角色 + 战斗 + 卡牌奖励 + 可解释建议」的 MVP 是**中高难度但可行**；做「全角色全流程稳定通关」是**月级甚至更久**，不适合作为开题承诺。

## 项目对象：Slay the Spire 2（StS2 EA）

本项目只面向 **《杀戮尖塔 2》（Slay the Spire 2, Early Access）桌面版**。

- 环境：**`Communication_Mod_STS2` + BaseLib + Harmony**，通过 `POST http://127.0.0.1:5000/update_state` 推送状态，并用 HTTP 响应体回传动作。
- 状态导出：除战斗外，还能看到卡牌奖励、商店、地图、营火、事件、宝箱和玩家库存；但**命令通道目前主要覆盖战斗动作**（`PlayCard` / `EndTurn`），奖励选择需要人工点击或自行扩展 mod。
- 版本：StS2 当前处于 EA（公开基准常用版本如 `v0.107.1`），最高 Ascension 为 10，共 5 名角色；卡池与平衡会随补丁变化，所有实验必须**锁版本**并记录 mod commit。
- 数据与工具只按 StS2 的 ID/字段设计。

## 推荐阅读顺序

- 只想快速判断值不值得做：读本页「先给结论」+ [开题分析第 1、3、9 节](../../docs/01-开题分析.md)。
- 准备马上动手：读 [项目规划第 4、8、10 节](../../docs/02-项目规划.md) 和 [日志的「下一步 7 天」](../../docs/LOG.md)。
- 准备接真实游戏：读 [真实接入与字段补全](../../docs/04-真实接入与字段补全.md)，然后按 [真实游戏接入操作清单](../../docs/05-真实游戏接入操作清单.md) 逐项执行。
- 要写论文/答辩：读 [开题分析第 2、4、5、8、10 节](../../docs/01-开题分析.md) 和文末「答辩预设问题」。

## 合规底线

- 仅本地单机使用，不向排行榜、成就、联机功能上传由自动化产生的成绩。
- 不读屏、不读内存、不注入未公开接口；只使用游戏公开的 mod 加载机制与本地回环 HTTP。
- 上游 mod 未见明确开源许可证，**不复制、不再分发**其代码或游戏资源；本项目只以依赖方式在本地使用，补丁也仅本地保留。
- 不抓取未授权的专家对局数据；使用自己录制的对局或取得明确许可的数据。
- 卡牌数据的来源与归属见 [NOTICE.md](NOTICE.md)。

## 当前状态

已完成：

- 开题分析、项目规划、调研与工具链核实。
- 战斗建议 MVP：87 张 Ironclad 卡牌库、状态适配、合法动作枚举、前向模型、beam 搜索、理由/备选、CLI、本地 HTTP bridge。
- 54 个单元/集成测试；离线示例、bridge 与 STS2MCP 实时战斗状态已验证。

未完成 / 待验证：

- 真实游戏 + `Communication_Mod_STS2` 的端到端联调；当前导出缺失 `Block` 等字段，需按 [LOG.md](../../docs/LOG.md) 的 D1–D3 验证。
- 选牌推荐 + 原因、卡牌奖励命令、遗物/药水建模、完整卡池与长程规划。
- 正式实验与胜率评估（当前只有规则单测和示例，不是胜率结论）。

详细进展与下一步见 [LOG.md](../../docs/LOG.md)。
# Slay-the-Spire2
