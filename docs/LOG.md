# SpirePilot 开发日志

> 记录「做了什么、为什么这样做、证据是什么、下一步做什么」。
> 只写事实；推测标注「待验证」；失败与负结果同样保留。

| 项目 | 内容 |
|---|---|
| 项目 | SpirePilot（尖塔领航员） |
| 游戏 | **Slay the Spire 2（StS2, Early Access）** |
| 当前阶段 | 战斗建议 MVP 已实现（合成状态验证）；真实游戏联调待做 |
| 当前版本 | v0.4（2026-10-22 09:46） |
| 日志时区 | Asia/Shanghai（UTC+8） |
| 记录规则 | 每个工作块开始/结束各记一条；格式 `YYYY-MM-DD HH:MM`；完成后立即写；早期记录按会话时间回填并标注 |
| 目标环境 | StS2 EA 桌面版 + BaseLib + Communication_Mod_STS2 |
| 默认范围 | Ironclad、A0–A10、战斗 + 卡牌奖励、copilot |

### 时间线

| 时间 | 事件 |
|---|---|
| 2026-10-21 22:33 | 立项，开始调研 |
| 2026-10-21 22:45 | 三份文档初稿完成 |
| 2026-10-21 22:54 | 开始战斗建议 MVP |
| 2026-10-21 23:30 | 战斗 MVP + 28 个测试完成 |
| 2026-10-22 09:27 | 开始真实接入工具链（D1–D3） |
| 2026-10-22 09:44 | 收到反馈：日志缺具体时间、项目名称需明确为 StS2、日志需即时更新 |
| 2026-10-22 09:46 | 完成名称/文档修正；41 个测试通过；日志即时更新 |
| 2026-10-22 09:52 | 说明 D1–D3 操作步骤；`make capture` 默认会话改为 d1 |
| 2026-10-22 09:59 | 新增可勾选的《真实游戏接入操作清单》 |
| 2026-10-22 10:08 | P0-1～P0-3 通过；P0-4 发现 ControlCe/AirPlay 占用 5000 |
| 2026-10-22 10:10 | 截图确认：设置项为「隔空播放接收器」；等待关闭后复测 |
| 2026-10-22 10:11 | P0 完成：关闭 AirPlay 后 5000 空闲；下一步 S1 |
| 2026-10-22 10:12 | 新增 P0-0：终端中文显示为 `?`，先修 UTF-8 locale |
| 2026-10-22 10:15 | P0-0 完成：UTF-8 正常；`ls docs` 显示 5 个文件 |
| 2026-10-22 10:17 | 新增《开发问题与解决方案》，记录 28 条问题 |
| 2026-10-22 10:51 | 游戏已能正常打开；进入 S1（确认存档 + 安装 mod） |
| 2026-10-22 10:54 | 用户报告游戏版本很低；S1 暂停，先确认/更新版本 |
| 2026-10-22 10:56 | Steam 显示 10:26 已更新，Build ID 23811903；澄清 0.x 是 EA 正常版本 |
| 2026-10-22 10:58 | 用户确认游戏已是最新版本；S1-0.5 完成，开始装 mod |
| 2026-10-22 11:01 | 确认 macOS mods 路径在 app 包内 `Contents/MacOS/mods` |
| 2026-10-22 11:04 | Finder 右键父文件夹无“显示包内容”；改为右键 app 或终端创建 |
| 2026-10-22 11:06 | `mods/` 已创建；BaseLib 三件套放错在 `MacOS/` 根目录 |
| 2026-10-22 11:10 | 用户尚未下载 Communication_Mod_STS2；提供仓库 ZIP 下载/复制步骤 |
| 2026-10-22 11:12 | `mods/` 已含 BaseLib 三件套和 Communication_Mod 文件夹 |
| 2026-10-22 11:15 | 用户找不到 Mods/Modding 菜单；说明在主菜单，并提醒 modded 存档独立 |
| 2026-10-22 11:19 | 截图确认是 Steam 库页面；游戏处于 `public-beta` 分支 |
| 2026-10-22 11:32 | Communication_Mod 报 manifest 迁移 + DLL 初始化失败；旧构建不兼容 |
| 2026-10-22 11:37 | 查替代方案：官方无 release；STS2MCP 可作为状态源但需 adapter |
| 2026-10-22 11:42 | STS2MCP 0.4.0 报 ReflectionTypeLoadException；疑似只支持 v0.103.2 |
| 2026-10-22 12:00 | 重编译 STS2MCP 出现 4 个 API 错误；补丁脚本已完成，待重建 |
| 2026-10-22 12:04 | STS2MCP 重编译完成，DLL/JSON 已复制到 mods；待启动验证 |
| 2026-10-22 12:05 | STS2MCP 加载成功：15526 返回 Hello v0.4.0；S1 完成 |
| 2026-10-22 12:08 | 抓到真实战斗状态；STS2MCP adapter + `live` 建议跑通 |
| 2026-10-22 12:13 | 修复 live 的 action/label 不一致；输出手牌索引与模拟连招 |
| 2026-10-22 12:17 | 讨论“训练/最优解/BFS-DFS/游戏内 UI”；更新问题记录到 33 条 |
| 2026-10-22 12:23 | 实现 exact-turn DFS；新增 live --solver exact/compare；48 测试 |
| 2026-10-22 12:29 | 完成竞品调研；新增 Phase A overlay、exact/beam 基准、docs/07；51 测试 |
| 2026-10-22 12:31 | 新增 2 回合 rollout 诊断；53 测试；Benchmark 仍显示 exact 未优于 beam |
| 2026-10-22 12:32 | 本轮自主优化收尾：调研/overlay/rollout/benchmark/日志完成 |
| 2026-10-22 12:32 | overlay GUI smoke test 通过（Tk 置顶窗口 + 实际进程启动） |
| 2026-10-22 12:39 | live compare 加入 2T rollout；15 状态三路基准仍无 rollout 优势 |
| 2026-10-22 17:59 | 分析 AI 双排视频；新增 docs/08 规划/难点；问题记录 35 条 |
| 2026-10-22 18:52 | 确认 STS2-Agent 已上 Steam 创意工坊：免费订阅、BYO 模型、需 0.111.0+ |
| 2026-10-22 19:26 | STS2-Agent overlay 已加载；macOS 中文字体显示为方块，记录为上游字体问题 |
| 2026-10-22 20:00 | 分析本地 STS2-Agent 源码；新增 docs/09 功能分析（UI/自动游玩/双排/API/MCP/可靠性） |
| 2026-10-22 21:39 | docs/09 增补技术栈分析：C#/.NET 9、Godot、HttpListener/SSE、LLM tool loop、Jev、双实例、MCP、测试契约 |
| 2026-10-22 21:45 | docs/08 增补基于 STS2-Agent 的工期估算：观战 1–2 周、AI 双排 3–5 周、稳定 2–3 月 |
| 2026-10-22 22:00 | 新增 docs/10 AI 双排执行规划：A/B/C 阶段、12 周计划、验收/风险/分工 |
| 2026-10-22 22:05 | 确认视频里的 Spire Buddy 是 xzmzm/spire-buddy；补进 docs/07，并区分其单实例定位与 STS2-Agent 双实例 |
| 2026-10-22 22:10 | docs/10 补充“策略可复用、co-op 适配不可直接复用”的边界 |
| 2026-10-26 09:10 | 新增开题报告 PPT 文案（26 页 + 附录）与配图/架构/路线图 |
| 2026-10-26 17:13 | 新增需求分析与开发流程：需求/分工/语音 owner/Sprint/文档清单 |
| 2026-10-26 17:20 | 新增语音/游戏模块拆解：V-01–V-15、G-01–G-12、依赖与本周任务 |
| 2026-10-26 17:22 | 用户确认 MVP=A；新增 Sprint 0 任务清单（7 天、五人分工、验收） |
| 2026-10-26 17:25 | 新增正式需求分析（SRS）：A 的功能/非功能/用例/数据/验收/待确认 |
| 2026-10-26 17:28 | 新增整体需求分析总册：A/B/C 全阶段功能组、优先级/Gate、数据/接口/风险 |
| 2026-10-26 17:35 | 确认双平台、B/C 必须做、macOS 存档；新增需求澄清与默认决策 |
| 2026-10-26 17:40 | 解释 Ironclad=铁甲战士、文字人格≠语音；确认 Must 边界与 UI |
| 2026-10-26 17:45 | 用户确认先做一个角色；需求冻结（Ironclad，A/B/C 全范围） |

---

## 2026-10-26 17:40｜按要求删除需求分析文档

- 用户要求：删除项目中的所有需求分析文档，改由用户指导重写。
- 已删除：
  - `docs/12-需求分析与开发流程.md`
  - `docs/15-需求分析-A观战搭子.md`
  - `docs/16-整体需求分析.md`
  - `docs/17-需求澄清与决策记录.md`
- 备份：`/tmp/spirepilot-requirements-backup-20261026-174022`
- 已清理 README/docs 中对应链接；保留开发/调研/开题/模块拆解等非需求文档。
- 下一步：等待用户给出需求分析模板/结构，再按用户指导重写。

---

## 2026-10-26 17:45｜需求冻结

- 用户确认：先做一个角色。
- 角色范围：**Ironclad（铁甲战士）**；C 阶段视时间再加角色。
- 其他已确认：双平台、B/C 必须做、Must 边界、A Tkinter/B/C Web、macOS 存档。
- 需求冻结日期：2026-10-26；新增功能走变更请求。
- 已更新 docs/16、docs/17、docs/14。

---

## 2026-10-26 17:40｜名词解释与需求确认

- 解释：
  - **Ironclad = 铁甲战士（角色）**，不是一张卡；当前有 87 张 Ironclad 卡数据。
  - **文字人格 = 文字表达风格**（名字/语气/口头禅/吐槽等级），不是语音；语音是 TTS 把文字读出来。
- 用户确认：
  - B/C 的 Must 边界接受；
  - UI：A 用 Tkinter、B/C 换 Web/overlay 接受。
- 仅剩角色范围待确认：是否先只做 Ironclad。
- 已更新 需求澄清与决策记录（已按要求删除）。

---

## 2026-10-26 17:35｜需求澄清与决策

- 用户确认：
  - 平台：macOS + Windows 双平台；
  - B/C 必须做；
  - 验收存档：macOS。
- 用户对状态源/UI/角色/人格/TTS 表示“不明白”；新增 需求澄清与决策记录（已按要求删除）：
  - 15526（STS2MCP）vs 8080（STS2-Agent）；
  - Tkinter vs 本地 Web；
  - Ironclad vs 全角色；
  - 模板 vs LLM；
  - 系统 TTS vs 豆包/云端。
  - 给出推荐默认，并把 B/C 的 Must 边界写清：不做全角色/全内容。
- 更新 docs/16 的平台、B/C 必须做、验收口径；docs/12/14/15 链接。

---

## 2026-10-26 17:28｜整体需求分析

- 新增 整体需求分析（已按要求删除）：
  - 产品定位、用户/场景、A/B/C 三阶段边界；
  - FR-GS/CB/CP/PR/VO/AP/CO/UI/CF/LG 十组需求；
  - Must/Should/Could、阶段 Gate、非功能、数据/接口、验收/不通过判定、风险/不做什么；
  - 明确 A 只读，B 才执行，C 才双人；A 先 Ironclad/15526/Tkinter 的假设。
- 已链接 README、docs/12/14/15。

---

## 2026-10-26 17:25｜A 观战搭子需求分析

- 用户明确：**现在只写需求分析，必须搞清楚**。
- 新增 A 观战搭子需求分析（已按要求删除）：
  - 文档边界：只写 A，不写排期/分工/代码；
  - 目标/非目标、用户故事、系统边界；
  - FR-S（状态）、FR-C（战斗）、FR-R（抓牌）、FR-P（文字人格）、FR-U（面板）、FR-L（日志）；
  - 输入字段、输出 JSON、数据质量规则；
  - UC-01–04、非功能需求、约束/假设、验收/不通过判定、追踪矩阵；
  - 明确 A 不执行动作、不写存档、不做 TTS/双人。
- 已链接 README、docs/12、docs/13。

---

## 2026-10-26 17:22｜确认 MVP=A，Sprint 0 启动

- 用户确认：**MVP = A 观战搭子**；不做自动打牌/TTS/AI 第二队友。
- 新增 [Sprint 0 任务清单](docs/14-Sprint0任务清单.md)：
  - 7 天：状态源 → 战斗建议 → 抓牌评分 → 面板 → 文字人格 → 测试 → 演示；
  - 五人分工：谭（接入/集成）、向（战斗/选牌）、马（文字人格+语音 spike）、付（面板/文档）、胡（测试/fixture+语音 spike）；
  - 语音 spike 明确不阻塞 A；马+胡共同做；
  - 状态源：先用 15526 STS2MCP，接口抽象 `StateSource`，STS2-Agent 后续接入。
- 更新 docs/12、docs/13 的确认状态与链接。

---

## 2026-10-26 17:20｜语音与游戏模块拆解

- 新增 [语音与游戏模块拆解](docs/13-语音与游戏模块拆解.md)：
  - 语音 V-01–V-15：策略、人格、文本清洗、TTS、播放、队列、字幕、设置、缓存、延迟、回退、合规；
  - 明确“马负责说什么、胡负责怎么出声、付负责怎么显示”，两人共同负责 `SpeechPipeline` 与测试；
  - 游戏 G-01–G-12：接入、状态、场景、合法动作、战斗、选牌、展示、自动、语音、双人、配置、评估；
  - P0（A）/P1（B）/P2（C）边界、依赖顺序、Sprint 0 任务与验收口径。
- 已链接 README、docs/12。

---

## 2026-10-26 17:13｜需求分析与开发流程

- 新增 需求分析与开发流程（已按要求删除）：
  - 需求边界：A 观战搭子优先，C 作为拉伸；
  - 功能需求 FR-01–FR-54、非功能需求、MoSCoW、MVP 验收；
  - 5 人分工：语音明确由马萌阳 + 胡真健负责；马负责“说什么”，胡负责“怎么出声”，付负责显示；
  - 2 周 Sprint、Git 分支、Code Review、Definition of Done、测试/发布流程；
  - 课程项目最小文档集 + 工程补充文档；
  - 12 周里程碑、风险表、5 个待确认问题。
- 已链接 README、docs/10。

---

## 2026-10-26 09:10｜开题报告 PPT 文案

- 新增 [开题报告：AI 双排助手](docs/11-开题报告-AI双排助手.md)：
  - 按 26 页 + 附录组织，含游戏介绍、四类功能、Spire Buddy/STS2-Agent/CombatSolver 对比、架构、创新、评估、12 周计划、五人分工、风险、Q&A；
  - 配图：`docs/assets/ppt/` 下 4 张游戏截帧 + 3 张自绘架构/功能/路线图；
  - 说明：游戏截帧来自用户提供的 `AI双排视频.mp4`，仅用于课程展示。
- 已链接 README、docs/10。

---

## 2026-10-22 22:05｜确认 Spire Buddy 是独立项目

- 通过 GitHub 搜索确认：视频顶部的 `Spire Buddy` 对应 `xzmzm/spire-buddy`（MIT，v1.2.11，Workshop 3802091577）。
- 它是**自包含 C# Mod**：游戏内 Chat/Settings、OpenAI-compatible、人格、Jev、CombatSolver 集成、ILSpy 进程内查规则、动作校验、JSONL trace。
- 关键区别：Spire Buddy 是单实例 in-process 助手/自动游玩；README 没有双实例 AI 队友能力。STS2-Agent 才是做“AI 控制第二个角色”的底座。
- 更正此前把视频直接归因于 STS2-Agent 的假设；已补进 [竞品与方案调研](docs/07-竞品与方案调研.md) 2.1.1。

---

## 2026-10-22 22:00｜AI 双排项目执行规划

- 新增 [AI 双排项目执行规划](docs/10-AI双排项目执行规划.md)：
  - 产品分 A 观战搭子 → B 双排参谋 → C AI 队友；
  - 技术路线：复用 STS2-Agent 的 co-op/overlay/API，SpirePilot 只做战术脑/人格层；
  - 12 周周计划、Phase 0–4、Gate 1–3；
  - 明确 A 用 1–2 周、C 用 3–5 周、稳定/语音 8–12 周；
  - 风险、验收指标、D1–D9 交付物、第一周具体任务。
- 已从 README、docs/08 链接；下一步按 Phase 0 先验证环境，再写观战搭子。

---

## 2026-10-22 20:00｜STS2-Agent 功能分析

- 用户把 `CharTyr/STS2-Agent` 克隆到本地 `STS2-Agent/`。
- 通读 README/中文 README、`PRODUCT_PLAN_CURRENT.md`、`docs/api.md`、`mcp_server/README.md`、UI/Agent/Game/Multiplayer/Server/Llm 源码目录。
- 新增 [STS2-Agent 功能分析](docs/09-STS2-Agent功能分析.md)：
  - 项目定位、版本/平台/许可证、代码规模；
  - Overlay、模型接入、自动游玩、双实例 co-op、对话/人格、HTTP API、MCP、数据、可靠性、测试；
  - 与 SpirePilot/CombatSolver 的对比；
  - 已知限制：LLM 战术上限、Windows 优先、macOS 中文字体、Mod 兼容、AGPL、版本绑定。
- 关键发现：当前源码 UI 已收束为 Play/Settings 两个主标签（README 六页描述略旧）；co-op/overlay/API 非常完整，但战斗决策没有确定性 solver。
- 已更新 README 文档索引。

---

## 2026-10-22 19:26｜STS2-Agent overlay 中文字体问题

- 用户截图：overlay 已成功打开（`127.0.0.1:8080 · human · F8`），说明 Mod 已下载并加载。
- 但所有中文显示为 `□`，英文 URL 正常；判断为 **CJK 字体 fallback 缺失**，不是编码/订阅/模型配置问题。
- 临时绕过：游戏语言切 English 后重启，overlay 应显示英文。
- 已补进 [docs/08](docs/08-AI双排规划与难点分析.md) 3.1；建议向作者反馈 macOS + 中文 + v0.16.2。
- 这也说明“中文 AI 队友”在 macOS 上有真实 UI 风险，后续我们若做 overlay 必须自带 CJK 字体/fallback。

---

## 2026-10-22 18:52｜确认 STS2-Agent 的 Steam 状态

- Steam 创意工坊页面：https://steamcommunity.com/sharedfiles/filedetails/?id=3796486050
- 性质：StS2 的 **Workshop Mod**，不是独立 Steam 商店游戏。
- 价格：**订阅免费**；但需要拥有 StS2，且不附带 API Key/托管模型，模型 token 自付。
- 要求：StS2 `v0.111.0` 或更新；支持 DeepSeek、硅基流动、ChatGPT、Ollama/LM Studio 等 OpenAI-compatible 端点。
- 页面数据（2026-10-22 抓取）：9 月 5 日发布、9 月 24 日更新，约 473 订阅。
- 平台注意：Workshop/发布包以 Windows 为主；macOS 可尝试源码构建，双开 co-op 需实机验证。
- 已同步到 [AI 双排规划](docs/08-AI双排规划与难点分析.md) 3.1 节。

---

## 2026-10-22 17:59｜AI 双排方向规划

- 分析用户上传的 `AI双排视频.mp4`（104s，1280×576）：StS2 双角色 co-op，顶部 `Spire Buddy`，AI 中文语音/字幕，实时点评遗物/事件/战斗，形态是“AI 队友”而非单纯聊天顾问。
- 新增 [AI 双排规划与难点分析](docs/08-AI双排规划与难点分析.md)：
  - 结论：这是 Agent，且是“具身协作 Agent”；
  - 现有 `STS2-Agent` 已实现双实例 co-op、游戏内 overlay、OpenAI-compatible、团队聊天/主动短评；
  - 建议不要从零造底座；优先复用/评估，差异化做中文人格 + 混合 solver + 可解释；
  - 分 A 观战搭子 → B 双排参谋 → C AI 队友；MVP 先做 A；
  - 给出 8–10 周里程碑、难点矩阵、验收标准、AGPL/语音/存档风险。
- 更新 README 顶部方向说明、docs/02 方向变更、docs/06 ISS-027（问题记录 35 条）。
- 关键判断：纯 LLM 做战术会蠢；必须“LLM 负责说什么，solver 负责做什么”。

---

## 2026-10-22 12:39｜三路对比与 rollout 结论

- `live --solver compare` 现在同时显示 exact / beam / 2T rollout。
- 15 个固定状态加入 rollout 后：
  - exact 与 beam 13/15 同动作；
  - rollout 2T HP 与 exact 首选动作的 2T HP 完全相同（0/15 提升）；
  - 说明当前 rollout 策略和状态集还不足以体现跨回合差异，不能据此切换默认决策。
- 结论：rollout 保留为诊断/overlay 信息，不替换默认 exact；下一步需要更真实的战斗状态集（不同敌人、牌组、遗物、能量）而不是继续在同一个 Nibbit 战斗上做变体。
- 测试 54 个通过。

---

## 2026-10-22 12:32｜本轮自主优化收尾

- 已完成：竞品调研（docs/07）、exact-turn DFS、exact/beam 基准、Phase A overlay、2 回合 rollout、51→53 测试、问题记录/日志更新。
- 关键指标：
  - 53 个测试通过；
  - 15 个固定状态 exact vs beam：13 同动作，2 个动作不同但 HP/掉血相同，exact 未表现出战损优势；
  - rollout fixture：防御线 HP 70、直接结束回合 HP 65，能区分短视线路。
- 仍开放：rollout 未接入默认决策、游戏内 mod overlay 未做、跨回合最优仍不保证、遗物/药水/复杂卡牌覆盖不足。
- overlay 已做 GUI smoke test：Tk 置顶窗口可创建，`spirepilot overlay` 进程可启动；实际画面内容仍需用户在场确认。
- 结论：本项目应定位为可解释 copilot/研究工具；不与 CombatSolver 比搜索强度。

---

## 2026-10-22 12:31｜2 回合 rollout 诊断

- 新增 `src/spirepilot/rollout.py`：
  - 对每个首动作，用 beam 策略补完当前回合；
  - 结算敌方、抽下一手，再跑 1–2 个回合；
  - 比较最终 HP / 敌方 HP，暴露“当前回合好、下一回合崩”的短视。
- 新增 `spirepilot rollout --turns 2` 和 `make rollout`。
- fixture 结果：防御/打击线 HP 70；直接结束回合 HP 65，说明 rollout 能区分短视线路。
- 当前 rollout 仅作诊断，不替换默认 exact；已接入 overlay 的 2T rollout 行。
- 测试 53 个通过。
- 下一步：做 exact vs beam vs rollout 的固定状态集评估，再决定是否切换默认。

---

## 2026-10-22 12:29｜竞品调研、Phase A overlay、exact/beam 基准

- 调研并记录 [竞品与方案调研](docs/07-竞品与方案调研.md)：
  - `CombatSolver`：成熟 StS2 0.111.0 求解器，跨回合、预算 beam、overlay、自动执行；我们不应正面比搜索强度。
  - `BoberInSpire`：C# mod + Python + Tauri 置顶窗口；Phase A 架构参考。
  - `sts2-card-advisor`：游戏内卡牌奖励 overlay，验证选牌 UI 需求。
  - `cvennevik/slay-the-spire-solver`：规则引擎+搜索，证明全状态穷举困难。
- 新增 `spirepilot overlay`：
  - Tkinter 置顶窗口，轮询 STS2MCP 15526；
  - 显示当前手牌、exact 推荐、beam 对比、预计 HP/敌 HP/掉血；
  - `make overlay` 启动。
- 新增 `scripts/benchmark_solvers.py`：
  - 15 个固定变体，exact vs beam；
  - 13/15 动作相同，2 个动作不同但 HP/掉血相同；当前小样本上 exact 没有表现出战损优势；
  - 结果写入 `experiments/20261022-exact-vs-beam.json`。
- 测试 51 个通过。
- 结论：下一步重点不是继续加大单回合 DFS，而是跨回合 rollout + overlay 可用性；否则会和 CombatSolver 做重复竞争。

---

## 2026-10-22 12:23｜exact-turn DFS 与对比模式

- 新增 `src/spirepilot/exact.py`：当前回合穷举 DFS，目标字典序为「胜利/存活 → 实际掉血少 → 敌方 HP 少 → 动作少 → 启发式分数」。
- 修复精确搜索的 action/label/sequence 同源问题，并用 `player_hp` 代替原始伤害做「掉血最少」比较。
- 新增 `--solver exact|beam|compare`：
  - `live` 默认 exact；
  - `compare` 同屏显示 exact vs beam 的动作、HP、敌方 HP、实际掉血；
  - `live --state <json>` 可对保存状态离线对比。
- 建议输出示例（fixture）：
  ```
  exact | 防御 | HP 84 | 敌 31 | 掉血 7
  beam  | 防御 | HP 84 | 敌 31 | 掉血 7
  ```
- 修复 reasons 中把「攻击 12」说成「受到 12 伤害」的歧义，改为「攻击 12，格挡 5，掉血 7」。
- 测试 48 个通过。
- 下一步：做置顶对比面板/overlay，并固定小状态集做 exact vs beam 正式对比。

---

## 2026-10-22 12:17｜训练、最优解与游戏内 UI 讨论

- 用户提出三个关键问题：
  1. 模型是怎么训练的？
  2. 建议应在游戏内显示，而不是敲命令。
  3. 目标是最少受伤 + 击倒，建议用 BFS/DFS 求最优。
- 澄清：当前没有训练任何 ML 模型；是「手写前向模型 + 手调评分 + beam search」。
- 设计结论：
  - 目标改成字典序：先胜利/存活，再最小掉血，再回合/资源；无胜利线时最大化存活。
  - 当前回合先做 **exact-turn DFS**，可验证 beam 的最优性差距；整场战斗用 DFS + 记忆化/分支限界，不用 BFS。
  - 隐藏抽牌下只能做「抽样内最优」或「期望最优」，不能承诺真实全局最优。
  - UI 分两阶段：先做置顶透明窗口/仪表盘读 15526；再做游戏内 BaseLib/Godot overlay。
- 已更新 [开发问题与解决方案](docs/06-开发问题与解决方案.md)：新增 ISS-021–025 和 OPEN-011–013，当前共 33 条。

---

## 2026-10-22 12:13｜修复 live 动作/标签不一致

- 用户反馈 `make live` 只显示「防御」，不知道打哪张。
- 检查发现两个问题：
  1. 输出只有动作名，没有手牌索引和完整连招。
  2. 搜索聚合有 bug：`best_action` 从 `root_actions` 取，但 `labels/sequence` 来自另一个样本，导致「推荐防御、手牌索引 0 是打击」。
- 修复：
  - `search` 改为始终用 `chosen.sequence[0]` 作为返回动作，保证 action/label 一致。
  - `format_advice_text` 增加当前手牌列表、手牌索引、模拟连招。
  - 加入回归断言：`advice.sequence[0] == advice.action`。
- 实测 live 输出现在会显示：
  ```
  当前手牌：
    [0] 防御  (1费)
    [1] 打击  (1费)
  下一步：打击 -> Nibbit 0
  手牌索引：2
  模拟连招：打击 -> Nibbit 0
  ```
- 44 个测试通过。

---

## 2026-10-22 12:08｜真实战斗状态 + STS2MCP adapter 跑通

- 用户进入 Ironclad 第一幕战斗，状态为 `state_type=monster`，敌人 `NIBBIT_0` 43 HP，意图 12 攻击。
- 抓到真实状态并保存为 `data/samples/sts2mcp_combat.json`。
- 新增 `src/spirepilot/sts2mcp.py`：
  - 解析 `battle/player/run` 树。
  - 手牌使用 `id`、`cost`、`can_play`、`target_type`。
  - 敌人使用 `entity_id`、HP、intents。
  - 中文牌堆名通过 `ironclad_names_zh.json` 映射。
- 新增 `spirepilot live`：从 `localhost:15526` 实时读取并给建议。
- 实测建议：回合 1，91/91 HP，3 能量，手牌 5，敌人 43 HP；推荐防御/打击线，带理由与警告。
- 测试 44 个通过。

---

## 2026-10-22 12:05｜STS2MCP 加载成功，S1 完成

- 游戏内提示：已加载模组 `STS2 MCP, BaseLib, Quick New Run & Quick Load`。
- 从开发机验证：
  - `GET http://127.0.0.1:15526/` → `Hello from STS2 MCP v0.4.0`, status ok。
  - `GET /api/v1/singleplayer` → `state_type=menu`, menu_screen=main。
- 说明补丁后的重编译可用；Communication_Mod 旧路线正式弃用。
- 下一步：进入单人战斗，抓 `/api/v1/singleplayer` 的 combat 状态，写 STS2MCP adapter。
- S1 标完成；S2 改为 STS2MCP 路线。

---

## 2026-10-22 12:04｜STS2MCP 重编译完成并复制

- 用户运行补丁后重新 `dotnet build` 成功。
- 已把 `out/STS2_MCP/STS2_MCP.dll` 和 `mod_manifest.json` 复制为 `mods/STS2_MCP.dll` / `STS2_MCP.json`。
- 本机尝试 `curl http://127.0.0.1:15526/` 返回 connection refused，说明游戏/mod 尚未启动或未启用。
- 下一步：启动游戏，接受 mod 加载，确认 Mods 列表里有 STS2 MCP；再 curl 根路径。
- 若根路径返回 Hello，再抓 `/api/v1/singleplayer` 状态给项目写 adapter。

---

## 2026-10-22 12:00｜重编译 STS2MCP：4 个 API 错误与补丁

- 用户安装 .NET 9、克隆 STS2MCP 后构建，出现 4 个 `CS1061`：
  - `StartRunLobby.MaxPlayers` 不存在。
  - `LoadRunLobby.ConnectedPlayerIds` 不存在（3 处）。
- 说明：当前游戏 API 已移除这些多人 lobby 字段；STS2MCP 0.4.0 源码是按旧 API 写的。
- 处理：新增 `scripts/patch_sts2mcp_api.py`，对 4 处做安全降级（多人元数据 fallback；单机战斗不受影响）。
- 已在临时副本上验证 4/4 替换成功；待用户在源码上执行并重新 `dotnet build`。
- 如果重建后还有别的 API 错误，继续逐个修。

---

## 2026-10-22 11:42｜STS2MCP 也报类型加载异常

- 用户已把 `STS2_MCP.dll` / `STS2_MCP.json` 放进 `mods/`，BaseLib 正常。
- 游戏报 `System.Reflection.ReflectionTypeLoadException`，加载 `STS2_MCP` 失败。
- 查 STS2MCP manifest：`version 0.4.0`；README 标注 tested against `v0.103.2`。
- 当前游戏更新到更新的版本（public-beta），DLL 引用的类型/方法可能已变化；不是路径问题。
- 可选项：
  1. 用 .NET 9 从源码重编译 STS2MCP（README 有 macOS 构建步骤）。
  2. 等作者更新 release。
  3. 把游戏切到 v0.103.2（不推荐，存档/beta 风险）。
- 清单 S1-4 仍为 `[!]`。

---

## 2026-10-22 11:37｜Communication_Mod 替代方案调研

- 官方 `Manuelbbl/Communication_Mod_STS2` 没有 releases；主分支预编译件仍是旧的 `v0.0.1`（2026-05-06），与当前游戏不兼容。
- 备选：`Gennadiyev/STS2MCP`，MIT，提供 released DLL + macOS 安装说明，HTTP API 在 `localhost:15526`；但 README 标注 tested against `v0.103.2`，对当前版本仍需实测。
- 如果采用 STS2MCP，需要给 SpirePilot 写新的 adapter（当前 adapter 面向 Communication_Mod 的 5000 端口协议）。
- 另一条路是本地重编译 Communication_Mod，但需要 .NET/Godot/游戏 DLL，成本更高。
- 清单 S1-4 仍为 `[!]`，等待用户选择替代方案。

---

## 2026-10-22 11:32｜Communication_Mod 初始化失败

- 用户已把 `Communication_Mod` 放进 `Contents/MacOS/mods/`，路径正确。
- 游戏报错：
  1. 模组清单需要迁移（mod manifest）。
  2. 程序集 DLL 初始化失败。
- BaseLib 正常加载；Communication_Mod 的 `.dll/.json` 文件时间为 2026-05-06，版本 `v0.0.1`，明显早于当前游戏版本。
- 判断：不是目录层级问题，是旧 mod 与当前游戏/BaseLib 的 manifest 格式与 DLL API 不兼容。
- 下一步：优先找兼容当前版本的 Communication_Mod 构建；其次尝试补 manifest 字段；最后才考虑重建/换方案。
- 清单 S1-3 完成，S1-4 标 `[!]`。

---

## 2026-10-22 11:19｜截图确认：还在 Steam 库，游戏在 public-beta

- 用户截图是 Steam 客户端库页面，不是游戏主菜单，所以看不到 Mods/Modding 是正常的。
- 需要先点绿色 **PLAY**，进入游戏标题画面。
- 截图显示 `VERSION public-beta`，说明当前在 beta 分支，不是 stable；先不动分支，避免影响 330 小时存档。
- 如果进入主菜单后仍没有 Mods/Modding，再考虑：备份存档 → 切到 `None`/stable → 更新 → 重试。
- 清单 S1-4 补充说明。

---

## 2026-10-22 11:15｜Mods / Modding 菜单位置

- 用户问「Mods / Modding 菜单在哪里」。
- 说明：不是 macOS 系统菜单，是 StS2 主菜单（标题画面）里的 **Mods / Modding / 模组** 按钮；可能在 Play/Settings 附近或底部。
- 如果看不到：完全退出游戏再启动；确认 `mods/` 在 `SlayTheSpire2.app/Contents/MacOS/mods`；确认 BaseLib 已加载。
- 重要：v0.107.1 首次带 mod 启动会使用独立的 modded 存档，看起来像新档；原版 330 小时存档仍在，不要重置/覆盖。
- 清单 S1-4 补充说明。

---

## 2026-10-22 11:12｜mods 目录结构基本到位

- `ls` 显示 `Contents/MacOS/mods/` 已包含：
  - `BaseLib.dll`
  - `BaseLib.json`
  - `BaseLib.pck`
  - `Communication_Mod/`
- BaseLib 三件套位置正确，S1-2 完成。
- 还需确认 `Communication_Mod/` 内部有 `Communication_Mod.dll` 和 `Communication_Mod.json`，且没有多套一层。
- 下一步：启动游戏，进 Mods 菜单启用两个 mod。

---

## 2026-10-22 11:10｜Communication_Mod 尚未下载

- 用户确认 `Communication_Mod` 还没下载。
- 处理：下载 `Manuelbbl/Communication_Mod_STS2` 仓库 ZIP，解压后取其中的 `Communication_Mod` 文件夹，复制到 `mods/`。
- 注意：不要复制整个仓库；不要多套一层 `Communication_Mod/Communication_Mod`。
- 清单 S1-3 标为进行中。

---

## 2026-10-22 11:06｜BaseLib 文件放错层级

- 用户截图显示：`mods/` 已创建在 `Contents/MacOS/`，但 `BaseLib.dll/json/pck` 与 `mods/` 平级，仍在 `MacOS/` 根目录。
- 正确：三件套必须放进 `Contents/MacOS/mods/`。
- Communication_Mod 文件夹也还没看到，需放进同一个 `mods/`。
- 处理：`mv BaseLib.* mods/`；再把 `Communication_Mod` 复制到 `mods/`。
- 清单 S1-1 勾选；S1-2/S1-3 进行中。

---

## 2026-10-22 11:04｜Finder 右键父文件夹没有“显示包内容”

- 用户截图：右键的是父文件夹 `Slay the Spire 2`，菜单只有“在新标签页中打开/显示简介/拷贝路径”等。
- 原因：`显示包内容`只对 `.app` 包出现；父文件夹不是 app。
- 处理：必须右键里面的 `SlayTheSpire2` app；或直接用终端创建 mods。
- 清单补充终端命令，避免继续卡在 Finder 菜单。

---

## 2026-10-22 11:01｜确认 macOS mods 路径

- 用户 Finder 截图显示游戏根目录只有 `controller_config` 和 `SlayTheSpire2` app，没有 `mods`。
- 查 BaseLib 官方 wiki：macOS 的 mods 路径是 `SlayTheSpire2.app/Contents/MacOS/mods`，不是 `steamapps/common/Slay the Spire 2/mods`。
- 处理：Finder 右键 app → 显示包内容 → Contents/MacOS → 新建 `mods`；再放 BaseLib 三件套和 Communication_Mod 文件夹。
- 提醒：Steam 更新可能覆盖/清除 app 包内文件；更新后要检查 mods，并保留一份外部备份。
- 操作清单/docs/04 已更正路径。

---

## 2026-10-22 10:58｜确认最新版本，开始 S1 装 mod

- 用户确认游戏已是最新版本。
- S1-0.5 完成；S1 继续，下一步安装并启用 BaseLib + Communication_Mod_STS2。
- 装 mod 前先退出游戏，避免文件占用/加载不到。
- 装好后先看 Mods 菜单，再启动 capture。

---

## 2026-10-22 10:56｜确认 Steam 已更新，澄清 Build ID

- 用户截图：Updates 页面显示 App ID `2868840`、Build ID `23811903`、Installed content updated `Oct 22, 2026 at 10:26 AM`。
- 判断：Steam 今天已经更新过；Build ID 是 Steam 内部构建号，不等于游戏语义版本。
- 需要澄清：StS2 是 EA，版本号 `0.x` 是正常的；例如 `v0.107.1` 不是“0.1 很旧”，而是 0.x 系列的第 107 个构建。
- 待用户确认游戏主菜单显示的具体版本字符串；如果主菜单是 `0.107.x` 一类，则版本正常，可以继续装 mod。
- 操作清单 S1-0.5 仍为进行中。

---

## 2026-10-22 10:54｜游戏版本过低，先更新再装 mod

- 用户报告当前 StS2 版本很低。
- 判断：旧游戏版本 + 新 BaseLib/Communication_Mod 很可能不兼容；不能直接装 mod。
- 处理：先查 `appmanifest_2868840.acf` / 游戏主菜单版本；在 Steam 中更新到当前 EA 版本。
- 若无法更新：不要硬装新版 mod，需找匹配旧版本的 BaseLib/mod，或先升级游戏。
- 清单新增 S1-0.5 并标 `[!]`；S1 暂停。

---

## 2026-10-22 10:51｜游戏已能启动，进入 S1

- 用户确认 StS2 现在能正常打开。
- 说明：启动成功不等于 Steam Cloud 冲突已彻底解决；先确认 330 小时进度在，并保留本地备份。
- 下一步：确认主菜单存档 → 安装/启用 BaseLib + Communication_Mod_STS2。
- 清单新增 S1-0 并勾选；S1 状态改为进行中。

---

## 2026-10-22 10:17｜新增《开发问题与解决方案》

- 新增 [开发问题与解决方案](docs/06-开发问题与解决方案.md)。
- 记录 28 条：20 条已解决、5 条已缓解/按设计、3 条未解决。
- 覆盖：StS1/StS2 工具链、缺失字段、HTTP 响应协议、卡牌数据、前向模型、搜索/理由 bug、AirPlay 端口、UTF-8、日志时间等。
- 附「仍未解决问题」表（真实游戏联调、Block、复杂卡牌、遗物/药水、胜率评估等）。
- README、docs/04、docs/05 已加入链接。

---

## 2026-10-22 10:15｜终端 UTF-8 修复完成

- 用户执行 `source ~/.zshrc` 后，`locale charmap` 输出 `UTF-8`。
- `ls docs` 正常显示 5 个中文文件名，P0-0 完成。
- P0-0～P0-4 已完成；P0-5 待安装后补版本。
- 下一步 S1：安装并启用 BaseLib + Communication_Mod_STS2。

---

## 2026-10-22 10:12｜终端 UTF-8（P0-0）

- 用户反馈 `ls docs` 显示 `01-????????????.md`；文件本身存在，问题在终端 locale/编码。
- 判断：`LANG` 为空或 `LC_CTYPE=C`，或 Terminal 编码不是 UTF-8。
- 处理：临时 `export LANG=zh_CN.UTF-8; export LC_CTYPE=zh_CN.UTF-8`；永久写入 `~/.zshrc`；Terminal 描述文件编码设为 Unicode (UTF-8)。
- 清单新增 **P0-0**，标 `[!]`；修好后改 `[x]`。
- 端口 P0-4 已完成，不受影响。

---

## 2026-10-22 10:11｜P0 完成，5000 端口释放

- 用户关闭「隔空播放接收器」后运行 `lsof -nP -iTCP:5000 -sTCP:LISTEN`，无输出。
- P0-1～P0-4 全部完成；P0-5 版本记录等安装后再填。
- 下一步：S1 安装并启用 BaseLib + Communication_Mod_STS2。
- 提示：用户终端当前在 `docs/`，运行 `make capture` 前要先 `cd ..` 回项目根目录。

---

## 2026-10-22 10:10｜截图确认：关闭「隔空播放接收器」

- 用户提供「隔空投送与接力」设置截图。
- 确认截图中的 **「隔空播放接收器」** 就是 AirPlay Receiver，蓝色开关处于开启状态。
- 处理：只关闭这个开关；「接力」和「隔空投送」可以保持原状。
- 关闭后等待几秒，运行 `lsof -nP -iTCP:5000 -sTCP:LISTEN`，应无输出。
- 操作清单与问答记录已更新；P0-4 仍为 `[!]`，等用户复测后改 `[x]`。

---

## 2026-10-22 10:08｜P0 预检结果：端口被 AirPlay 占用

### 10:08 用户执行结果

- [x] P0-1：终端在项目根目录，`make`/`python3` 可用。
- [x] P0-2：`make doctor` 通过；卡牌库 87 张；示例建议 Thunderclap。
- [x] P0-3：`make test` 通过；41 个测试全部 OK。
- [!] P0-4：`lsof -nP -iTCP:5000 -sTCP:LISTEN` 显示 `ControlCe` 占用 `*:5000`。

### 判断

`ControlCe` 是 macOS ControlCenter，`*:5000` 是 **AirPlay 接收器（AirPlay Receiver）**，不是本项目进程。macOS 默认会占用 5000，导致 `make capture` 无法绑定。

### 处理

1. 系统设置 → 通用 → 隔空投送与接力 → 关闭「AirPlay 接收器」。
2. 重新运行 `lsof -nP -iTCP:5000 -sTCP:LISTEN`，应无输出。
3. 再运行 `make capture`。
4. 不建议直接 `kill 7375`：ControlCenter 可能自动重启，也会影响 AirPlay。

### 文档更新

- [x] 操作清单：P0-1～P0-3 勾选；P0-4 标 `[!]`；增加阻塞说明、问答记录、排错行。
- [x] 当前状态：P0 进行中，阻塞在端口。
- [x] 提醒：`docs/05` 于 09:59 创建；若刚才 `ls docs` 没看到，重新 `ls`。终端显示 `?` 是 locale/编码，不影响文件。

---

## 2026-10-22 09:59｜真实游戏接入操作清单

### 09:59 完成

- [x] 新增 [真实游戏接入操作清单](docs/05-真实游戏接入操作清单.md)。
- [x] 清单拆成 P0 预检、S1 安装/启用、S2 capture、S3 inspect、S4 发材料。
- [x] 每项带编号（P0-1、S1-2…）、checkbox、命令、期望输出与排错。
- [x] 增加「进度记录」「问答记录」「版本记录」「快速排错」四张表。
- [x] 明确更新方式：用户回复编号/结果，我改 `[x]`/`[!]` 并补时间。
- [x] README、docs/04 已加入清单链接。

### 待用户执行

- P0 预检
- S1 安装并启用 StS2 + BaseLib + Communication_Mod_STS2
- S2 `make capture` 打 2–3 回合
- S3 `inspect` 审计
- S4 把报告/文件发我

---

## 2026-10-22 09:52｜D1–D3 操作步骤说明

- 用户确认下一步是否就是「安装/启动 → capture 打 2–3 回合 → inspect 报告」；回答：是，顺序正确。
- 发现文档写 `data/replays/d1`，但 `make capture` 原来使用时间戳会话；已把 Makefile 改为 `SESSION ?= d1`，`make capture` 现在固定写到 `data/replays/d1/`。
- 下一步等用户提供真实游戏状态；若本机无法安装，则先做选牌推荐规则版。

---

## 2026-10-22 09:44｜反馈处理：日志时间与项目名称

### 09:44 收到反馈

- 日志只有日期，没有具体时间。
- 需要明确：项目是 **Slay the Spire 2**，不是 StS1，也不保留 StS1 分支。
- 日志需要即时更新，不能等阶段性结束后补写。

### 09:44–09:46 处理

- [x] 日志头部增加时区、记录规则与时间线。
- [x] 新增本条目，记录反馈原文与处理动作。
- [x] 检查 README / 开题 / 规划中的 StS1 表述，删除“如果实际目标是 StS1”的附录与 S1 工具引用。
- [x] 把项目对象统一为 **Slay the Spire 2（StS2 EA）**。
- [x] 完成后再补一条带具体时间的收尾记录（本次会话内）。

### 09:46 完成

- [x] README：删除“关键修正（StS1）”整段，改为「项目对象：Slay the Spire 2（StS2 EA）」。
- [x] docs/01：删除「如果实际目标是 StS1」附录 A、[13]–[15] 三条 StS1 工具参考；附录重编号为 A/B。
- [x] docs/02：G0 退路从“换 StS1”改为“暂停自动执行 / 纯离线回放”。
- [x] docs/04：去掉含拼写错误的绝对路径，改为 `<项目根目录>`。
- [x] LOG：加时区、时间线、记录规则；本条目记录 09:44 反馈与 09:46 完成。
- [x] 复跑 41 个测试、链接检查、trace；均通过。

### 证据

```bash
date '+%Y-%m-%d %H:%M:%S %Z (%z)'
# 2026-10-22 09:44:21 CST (+0800)

PYTHONPATH=src python3 -m unittest discover -s tests -q
# Ran 41 tests ... OK

PYTHONPATH=src python3 -m spirepilot trace --trace data/traces/basic_thunderclap.json
# [PASS] step 1
```

### 待办

- 清理 README 的“关键修正（StS1）”段落。
- 删除 docs/01 的 StS1 附录 A 与 [13]–[15] 参考。
- 修改 docs/02 G0 中“换 StS1”的退路。
- 明确 docs/04 的路径写法，不再把目录拼写当作项目名。

---

## 2026-10-21 22:33｜立项与调研

> 22:33 开始；22:45 三份文档初稿。早期分钟按会话时间回填，标注“约”。

### 目标

1. 判断「战斗内最优出牌 + 选牌推荐/解释」分别属于什么问题、算不算 agent、难度如何。
2. 核实项目对象为 **Slay the Spire 2（StS2）**，避免沿用错误工具链。
3. 产出开题分析、项目规划、开发日志三份文档，作为后续执行的基线。

### 完成

- [x] 检查工作区：项目根目录（沿用现有文件夹名），初始为空目录。
- [x] 根据项目要求与当前生态，确定目标为 **Slay the Spire 2（StS2 EA）**。
- [x] 核实 StS2 的通信方案：`Communication_Mod_STS2` + BaseLib + Harmony，本地 HTTP `127.0.0.1:5000`。
- [x] 阅读上游 mod 源码（`MainFile.cs`、`LootScanner.cs`、`NonCombatScanner.cs`、`VirtualController.cs`、`server.py`），确认状态字段与命令通道的实际能力/缺口。
- [x] 调研 2026 年 10 月前后的公开工作：SpireClimb、SpireBench、AgenticSTS、Orak、Language-Driven Play，以及 `sts2-mcp-bridge`。
- [x] 撰写 [README.md](README.md)、[开题分析](docs/01-开题分析.md)、[项目规划](docs/02-项目规划.md)。
- [x] 建立本日志，记录决策、证据、风险与下一步。
- [x] 做文档一致性校验：本地链接、引用编号、表格列数、代码围栏；修正 StS2 工具链、模拟器可得性、随机种子/Boss 数据缺口等表述。

### 关键事实与证据

#### 1. 环境与版本

- StS2 处于 Early Access；Epoch 的 SpireClimb 基准把版本冻结在 **v0.107.1**，最高难度为 **Ascension 10**，共 **5 名角色**（Ironclad、Silent、Regent、Necrobinder、Defect）[^1]。
- 这意味着：所有实验必须锁版本；跨版本比较没有意义；卡牌/遗物数据要带版本号。

#### 2. 通信链路

- 上游 mod 通过 `POST http://127.0.0.1:5000/update_state` 推送 JSON；游戏侧把 HTTP 响应体当作命令执行[^2]。
- 命令执行器目前只处理：
  - `{"Type":"PlayCard","HandIndex":N,"TargetIndex":M}`
  - `{"Type":"EndTurn"}`
- 上游示例 `server.py` 返回 `{"status":"OK"}`；这个响应体会被游戏当作命令解析（然后失败）。**我们的 bridge 必须直接返回动作 JSON，或在无动作时返回 `OK`/空**。这是一个实际集成坑，已写入项目规划 §4.6。
- 上游 README 说只导出战斗状态，但源码里的 `LootScanner` 与 `NonCombatScanner` 还导出了：
  - `Loot`（卡牌奖励、金币、药水、遗物）
  - `SHOP_INITIAL` / `SHOP_UPDATE`
  - `Campfire`
  - `Event`
  - `Treasure_Initial` / `Treasure_Update`
  - `MapGenerated` / `Focus`
  - `PlayerInventory`
- **但是**：非战斗状态目前主要是「可读」，没有对应的「可选择」命令；奖励三选一仍需人工点击或自行扩展 mod。项目因此默认「选牌建议 + 人执行」，而不是自动选牌。

#### 3. 状态字段缺口（重要）

阅读 `MainFile.cs` 后确认，当前导出**不完整**，至少包括：

| 需要的字段 | 当前导出 | 影响 |
|---|---|---|
| 玩家格挡 | 未见 | 无法准确计算敌方伤害/格挡收益 |
| 敌人格挡 | 未见 | 无法准确计算斩杀线 |
| 手牌费用/是否可打出/是否升级 | 手牌只有 `Card_Name`/`Card_ID` | 无法直接枚举合法动作，需查 `CardCatalog` 或扩展 |
| 卡牌完整文本/稀有度/类型/标签 | `CardCatalog` 只有部分数值字段 | 解释与选牌特征需要自建数据库 |
| 药水使用 | 有药水列表，无使用命令 | MVP 不含药水 |
| 奖励选择 | 有 `Rewards` 数据，无选择命令 | 只能建议，不能自动选 |
| Boss/后续路线 | `FirstInfo` 只有 Act/Floor；`MapGenerated` 主要是房间类型 | 选牌特征中的 Boss/路线信息需另补或降级 |
| 随机种子/RNG | 未见 | 无法精确复现随机结果；固定种子需外部记录或工具 |
| 敌人意图完整参数 | 有 `Type/Damage/Hits`，但可能缺附加效果 | 需要逐敌人核对 |

> 结论：**环境接入不是「一天跑通就完事」**。能收到状态只是第一步；要做出可信的 solver，必须补字段或从日志推导。项目规划把「状态字段审计/扩展」列为 W0–W2 的硬任务。

> **对上一版工作量的修正**：上一版把战斗 solver 估为 2–4 周，隐含了「有现成模拟器」的前提。StS2 未找到成熟、可公开复用的战斗模拟器，前向模型本身就是 35–45 h 的独立工作包；因此本项目把战斗部分整体按 4–6 周估，而不是 2–4 周。

#### 4. 数据稳定性

- `Card_ID` 在 `Piles` 中使用 `k.GetHashCode()`，这是运行时 hash，**不是稳定主键**；不能跨局/跨版本用来关联卡牌。持久化应使用 `card_id + upgraded + pile + position`。
- 手牌索引会在每次出牌后变化；不能缓存索引，必须每次从最新状态重算。
- 同一状态可能重复推送；需要按 `(TurnNumber, ActionCount)` 或状态 hash 去重。

#### 5. 公开研究的关键数字

- SpireClimb：StS1 A0 前沿模型胜率 90%+；A20 Heart 约 17%–20%；StS2 A10 模型胜率 25%+；顶尖人类在 StS1 约 85%、StS2 约 95%[^1][^3]。
- SpireBench：20 个 LLM 在 StS1 A20 上能超过规则基线与普通人类，但远逊专家；强弱差距主要在战斗；模型普遍过度保守；跨局记忆没有稳定收益[^4]。
- AgenticSTS：StS2 A0，无记忆 10 局赢 3 局，加技能层 10 局赢 6 局，`p≈0.37`，只能算方向性证据[^5]。
- `sts2-mcp-bridge`：规则 autopilot，无 LLM；作者明确写「只端到端验证 1 场战斗，没有胜率指标」，动态费用/部分能力未完整建模[^6]。
- Orak：12 款游戏、MCP 接口；StS 中 LLM 主要做战斗与卡牌奖励，其余由规则 bot 完成，覆盖有限[^7][^4]。
- Language-Driven Play：早期验证 LLM 作为 StS agent 的可行性，同时暴露长程规划/数值精确性短板[^8]。

#### 6. 对「是不是 agent」的判断

- 战斗 solver：本质是 POMDP/单人随机博弈下的限定预算求解器；单独看是**决策器**，不是 agent。
- 选牌推荐：本质是上下文感知排序/可解释推荐；单独看是**推荐器**，不是 agent。
- 读状态 → 决策 → 执行 → 看反馈 → 再决策：闭环后才算 agent。
- LLM 只写解释：不是 LLM agent；LLM 真正掌决策并调用工具：才是 LLM agent。
- 本项目默认 **L2 copilot**；自动战斗是可选 L3；不把「用了 LLM」当成 agent 的判据。

### 决策记录（ADR）

#### ADR-001：项目对象锁定 Slay the Spire 2（StS2）

- **背景**：项目对象是 **Slay the Spire 2（StS2 EA）**；早期调研一度出现 StS1 工具，需要排除。
- **决策**：只做 StS2 EA；不保留 StS1 兼容层或 StS1 附录。
- **后果**：需要面对 EA 版本漂移、字段缺口、卡池变化；但题目与目录一致，且 StS2 的研究空间更新。
- **状态**：已采纳；项目范围只包含 StS2。

#### ADR-002：混合架构，LLM 不掌战术裁决

- **背景**：LLM 先验强、解释好，但高难度战术不稳、成本/延迟高、易幻觉；搜索精确但缺常识与解释。
- **决策**：战斗用「前向模型 + beam/expectimax + 评估函数」；选牌用「上下文特征 + 排序器」；LLM 只做证据约束的自然语言解释。
- **后果**：工程复杂度上升，但每层可独立评估；避免把系统成败押在 LLM 上。
- **状态**：已采纳。

#### ADR-003：默认 copilot，自动执行可选

- **背景**：用户要的是「推荐 + 原因」，不一定是全自动；自动执行有合规与误操作风险。
- **决策**：默认人机协同；自动战斗作为 L3 可选模式，必须显式开启、可一键关闭。
- **后果**：需要同时支持「建议」与「执行」两种响应；评估要区分 shadow 与 auto。
- **状态**：已采纳。

#### ADR-004：首期只做 Ironclad + 战斗 + 卡牌奖励

- **背景**：12 周单人；全角色/全流程不可行；前向模型是最大工作量。
- **决策**：单角色、A0–A10、第一幕核心卡/敌人；不做地图/商店/事件/营火全自动。
- **后果**：结论的外部效度有限；需要在报告中明确边界。
- **状态**：已采纳。

#### ADR-005：解释必须「证据约束 + 校验」

- **背景**：LLM 解释容易编数值/机制；用户要的是「原因分析」，不是「听起来像原因」。
- **决策**：先产生结构化证据包，再生成解释；校验卡名/数值/机制/引用；失败回退模板。
- **后果**：解释可能不如自由生成「流畅」，但更可信、可审计。
- **状态**：已采纳。

#### ADR-006：不复制、不再分发上游 mod

- **背景**：`Communication_Mod_STS2` 未见明确开源许可证；`sts2-mcp-bridge` 的 README 也注明其上游 mod 为「no explicit license — all rights reserved」[^6]。
- **决策**：只以本地依赖方式使用；如需补字段，补丁仅本地保存，不进入公开仓库；不提交游戏资源/二进制。
- **后果**：复现需要用户自行安装 mod；文档要写清楚依赖步骤。
- **状态**：已采纳。

#### ADR-007：用「近似最优」而不是「最优」

- **背景**：抽牌顺序隐藏、随机性存在、计算预算有限；严格最优不可证明。
- **决策**：统一表述为「信息集下、限定预算内的近似最优」；在小状态上用穷举报告最优性差距。
- **后果**：避免过度承诺；答辩时需解释定义。
- **状态**：已采纳。

### 产出文件

| 文件 | 内容 | 状态 |
|---|---|---|
| [README.md](README.md) | 项目索引、结论、快速开始、合规底线 | v0.4 |
| [docs/01-开题分析.md](docs/01-开题分析.md) | 背景、现状、RQ、形式化、技术路线、评估、风险、参考文献 | 初稿 |
| [docs/02-项目规划.md](docs/02-项目规划.md) | 范围、架构、模块、数据、12 周计划、测试、验收 | 初稿 |
| [docs/03-战斗建议MVP说明.md](docs/03-战斗建议MVP说明.md) | 战斗 MVP 的代码结构、数据流、搜索/评估、限制 | 已实现 |
| [docs/04-真实接入与字段补全.md](docs/04-真实接入与字段补全.md) | D1–D3 runbook、字段清单、本地 patch 建议 | 已实现 |
| [src/spirepilot/](src/spirepilot/) | 战斗建议 + 接入/审计/trace 代码包 | v0.4 |
| [data/cards/ironclad_cards.json](data/cards/ironclad_cards.json) | 87 张 Ironclad 机械字段 | 已生成 |
| [data/traces/basic_thunderclap.json](data/traces/basic_thunderclap.json) | 合成 golden trace | 已生成 |
| [LOG.md](LOG.md) | 本日志 | 持续更新 |

### 未完成 / 不要误读

- **没有**安装游戏、BaseLib 或 `Communication_Mod_STS2`；真实游戏联调未做。
- **没有**抓取过真实游戏状态；当前只验证了合成状态，字段分析来自上游源码。
- **已经**写了战斗建议 MVP（不是只有文档）；但还没有胜率结论。
- **没有**验证 macOS/当前游戏版本与 mod 的兼容性。
- **没有**专家选牌数据；选牌推荐尚未实现，评估方案是计划。
- 文档中的胜率数字来自公开基准，不是本项目实验。

### 阻塞与待验证

| 项 | 状态 | 验证方式 |
|---|---|---|
| 当前游戏版本是否与 mod 兼容 | 待验证 | 安装后跑 `server.py`，看是否收到 POST |
| 玩家/敌人格挡等字段能否补 | 待验证 | 扩展 scanner 或查 `CombatState` 反射字段 |
| 奖励选择是否有隐藏命令 | 待验证 | 读源码/试 UI 输入；默认人工 |
| macOS 是否支持该 mod | 待验证 | 本地安装测试；不行则换 Windows 环境 |
| 卡牌文本/标签来源 | 待验证 | 本地化文件、mod 导出、wiki（注意许可） |
| 专家选牌数据 | 未开始 | 自采 + 志愿者；先做 100–200 条 |

### 风险快照（当前最担心的三件事）

1. **状态字段不全**：没有格挡/费用/可打出，solver 再强也无法算准。优先级最高。
2. **前向模型保真**：StS2 未找到成熟、可公开复用的战斗模拟器；卡牌/遗物/能力组合多，容易出现「模拟里赢了，游戏里输了」。
3. **评估样本不足**：单局随机大，12 周内很难跑出统计显著的胜率差异；需要配对种子与置信区间，接受方向性结论。

### 下一步 7 天

> 详细版见 [项目规划 §15](docs/02-项目规划.md)。这里只列检查点。

- [ ] D1：确认游戏版本；安装 BaseLib + Communication_Mod_STS2；跑通 `server.py`。
- [ ] D2：抓 20 条战斗状态 + 1 次奖励状态；对照源码列字段缺口。
- [ ] D3：返回 `PlayCard`/`EndTurn`，确认游戏执行；记录拒绝情况。
- [x] D4：写 `doctor` 脚本，检查版本/端口/关键字段（合成状态已通过）。
- [x] D5：建 Ironclad 卡牌库 v0（已扩到 87 张机械字段；待真机核对）。
- [ ] D6：用 1 条真实轨迹跑 golden trace 重放骨架（合成 trace 已跑通，真实待数据）。
- [ ] D7：G0 复盘；更新本日志与风险。

### 当日结论

- **问题类型**：战斗 = 序贯决策/限定预算求解；选牌 = 上下文推荐 + 可解释 AI。
- **是不是 agent**：单独模块不是；闭环自动执行才是；默认做 copilot。
- **难度**：12 周单角色 MVP 中高但可行；全流程自动通关是月级，不做承诺。
- **最关键的不是算法，而是状态字段 + 前向模型 + 评估协议**。这三件事决定项目是否可信。

---

## 2026-10-21 22:54｜战斗建议 MVP 实现

> 22:54 开始；23:14 测试；23:30 收尾。

### 目标

在文档完成后，先落地「战斗内建议」的最小可用闭环，不碰选牌推荐：

```text
JSON 战斗状态 -> 解析 -> 合法动作 -> 前向模型 -> beam 搜索 -> 推荐 + 理由 + 备选
```

### 完成

- [x] 从 Spire Codex 公共 API 导入 **87 张 Ironclad 卡牌**机械字段，生成 `data/cards/ironclad_cards.json`。
- [x] 建 `src/spirepilot/` 包：模型、卡牌库、状态适配、规则/前向模型、评估、搜索、建议、CLI、HTTP bridge。
- [x] 实现 `CardCatalog` 回退：未知卡/升级卡优先用游戏实时字段覆盖静态库。
- [x] 实现合法动作枚举、目标选择、`EndTurn`、抽牌顺序采样、beam 搜索、根动作聚合。
- [x] 实现理由生成：首步效果、连招总伤害/格挡/抽牌、击杀、敌方回合后 HP、剩余敌方血量、近似警告。
- [x] 实现 CLI：`doctor` / `demo` / `advise` / `serve`。
- [x] 实现 bridge：copilot 默认回 `OK`，`--auto` 回 `PlayCard`/`EndTurn`，支持 `/health` 与 JSONL 决策日志。
- [x] 建合成状态 `data/samples/combat_basic.json`（2 敌人、5 手牌、3 能量、1 力量）。
- [x] 写 41 个单元/集成测试，全部通过。
- [x] 更新 [README.md](README.md) 与新增 [战斗建议 MVP 说明](docs/03-战斗建议MVP说明.md)。

### 验证证据

```bash
PYTHONPATH=src python3 -m spirepilot doctor
# 卡牌库：87 张
# 示例建议：Thunderclap（全体）
# OK

PYTHONPATH=src python3 -m spirepilot demo
# 推荐：Thunderclap（全体）
# 理由：AOE 伤害、施加易伤、连招 43 伤害、击杀 Louse、敌方回合后 HP 47/80
# 备选：Thunderclap / Defend / Pommel Strike ...

PYTHONPATH=src python3 -m unittest discover -s tests -v
# Ran 41 tests ... OK
```

HTTP 冒烟（合成状态）：

```text
POST /update_state -> OK              # copilot
POST /update_state -> {"Type":"PlayCard","HandIndex":2,"TargetIndex":0}  # --auto
GET  /health       -> {"ok": true, "cards": 87, ...}
```

### 本次新增决策（ADR）

#### ADR-008：静态卡牌快照 + 实时 `CardCatalog` 覆盖

- **背景**：完全依赖游戏导出会受字段缺失影响；完全依赖静态库会受 EA 版本漂移影响。
- **决策**：静态库存机械字段；解析时若 `CardCatalog` 有对应字段，覆盖费用/伤害/格挡/抽牌/能量/目标/易伤/虚弱。
- **后果**：离线可用、在线更准；但升级牌/多版本仍可能不一致，报告中要标注来源。

#### ADR-009：限定预算 beam + 启发式评估，不做 RL

- **背景**：没有可复用的 StS2 模拟器；12 周内 RL 不现实。
- **决策**：自建近似前向模型，默认深度 5、宽度 18、650ms/步、2 个抽牌样本；超时降级。
- **后果**：可解释、可复现、可在本地跑；但不是理论最优，复杂卡牌/遗物仍会偏差。

#### ADR-010：copilot 默认，自动执行显式开启

- **背景**：用户要的是建议；自动执行有误操作与合规风险。
- **决策**：`serve` 默认只打印建议并回 `OK`；`--auto` 才回动作，且只覆盖战斗。
- **后果**：人机协同可独立交付；自动模式作为可选实验。

#### ADR-011：首期只做 Ironclad + 当前回合

- **背景**：全角色/长程规划超出 MVP；先证明战术层有价值。
- **决策**：单角色、单回合 + 敌方回合评估；不做药水/遗物/选牌。
- **后果**：结论外部效度有限；但代码与评估接口可扩展到后续。

#### ADR-012：前向模型不追求「全卡精确」，但必须标注近似

- **背景**：87 张卡全精确建模成本高，且没有金标对拍。
- **决策**：直接效果 + 常见能力优先；未知能力按估值计入，输出统一警告。
- **后果**：建议可用但不宣称精确；下一步用金标轨迹逐卡修正。

### 已知限制（本次未解决）

| 限制 | 影响 | 下一步 |
|---|---|---|
| 未安装真实游戏/mod | 所有状态来自合成样本 | D1–D3 联调 |
| 上游导出可能缺 `Block` | 攻防估值偏差大 | 扩展 scanner 或影子状态推导 |
| 遗物/药水未建模 | 部分局面估值偏低/偏高 | 先记录，后补常见遗物 |
| 强化牌数值不完整 | 升级牌按基础值 | 用 `CardCatalog`/升级表覆盖 |
| 复杂卡牌只做近似 | 个别建议可能错 | 金标对拍 + 逐卡修正 |
| 没有胜率评估 | 只有规则/集成测试 | 固定种子 + 贪心基线 |

### 下一步

1. **真实联调**：安装 BaseLib + `Communication_Mod_STS2`，抓 20 条状态，验证 `Block`/费用/目标字段。
2. **补字段**：优先把玩家/敌人 `Block`、手牌 `Cost/CanPlay` 导出；否则建议精度受限。
3. **金标对拍**：录 10–20 条真实动作，逐步比对 HP/格挡/能量/抽牌。
4. **基线评估**：贪心 vs beam，固定战斗集，报告掉血/胜率/延迟。
5. **再往选牌推荐走**：先有稳定战斗 solver 和回放，再做卡牌奖励。

---

## 2026-10-22 09:27｜真实接入工具链（D1–D3）

> 09:27 开始；09:40 工具链完成；09:44 收到反馈并转入修正。

### 目标

继续 D1–D3：在无法直接安装游戏的环境里，先把「抓状态 → 审计字段 → 回放对拍」的工具链做完，等游戏可用时按 runbook 执行。

### 完成

- [x] `spirepilot inspect`：审计原始状态，检查 Player/Block/费用/CanPlay/Catalog/敌人 Block/意图/索引/版本等字段。
- [x] `spirepilot capture`：独立 HTTP capture，只保存 `states.jsonl` + `meta.json`，回 `OK`，不执行动作。
- [x] `spirepilot trace`：golden trace 回放，与期望状态逐字段对比（HP/能量/手牌/牌堆/敌人/能力），缺字段跳过并 warning。
- [x] 新增 `data/traces/basic_thunderclap.json` 合成 trace，验证 trace 框架。
- [x] 新增 [真实接入与字段补全](docs/04-真实接入与字段补全.md)：D1–D3 runbook、字段清单、本地 patch 位置、影子状态降级方案。
- [x] 测试从 28 增加到 40，全部通过。

### 验证证据

```bash
PYTHONPATH=src python3 -m spirepilot inspect --state data/samples/combat_basic.json
# 结论：degraded；Player.Block OK；手牌 Cost/CanPlay/IsUpgraded 缺失；Catalog 覆盖 5/5

PYTHONPATH=src python3 -m spirepilot inspect   --capture data/replays/d1/states.jsonl --index -1
# 支持直接审计 capture 的最后一条

PYTHONPATH=src python3 -m spirepilot trace   --trace data/traces/basic_thunderclap.json
# [PASS] step 1: Thunderclap（全体）；error diff 0

PYTHONPATH=src python3 -m unittest discover -s tests -v
# Ran 41 tests ... OK
```

### 本次新增决策（ADR）

#### ADR-013：capture 与 serve 分离

- **背景**：真实接入第一步是抓数据，不应同时做建议/执行；两者都占 5000 端口。
- **决策**：`capture` 只记录并回 `OK`；`serve` 才做建议/自动动作。
- **后果**：D1–D3 的数据采集与决策调试解耦，减少误操作。

#### ADR-014：trace 对缺失字段跳过，不伪造对比

- **背景**：上游可能不导出 Block/费用；如果 trace 用默认 0 对比，会误判模型正确。
- **决策**：期望状态没有的字段跳过并 warning；只对真实存在的字段做严格对比。
- **后果**：trace 报告更保守，但不会给出虚假的「全部通过」。

#### ADR-015：不在未编译环境里盲写 mod 补丁

- **背景**：当前没有 `sts2.dll`，无法编译验证 C# 属性名。
- **决策**：docs/04 只给「字段位置示意」，不提交可直接套用的 patch；优先 capture + inspect，再决定本地 fork。
- **后果**：避免给出编译不过的补丁；真实接入时仍需 D3 编译验证。

### 限制

- 仍未安装真实游戏，无法执行 D1–D3 的实际抓包。
- 合成 trace 只能验证 trace 框架，不能证明前向模型与游戏一致。
- `inspect` 的「degraded」是预期结果：上游手牌缺费用/可打出字段。

### 下一步

- 有游戏环境：按 [docs/04](docs/04-真实接入与字段补全.md) 执行 D1–D3，抓 20 条战斗状态 + 5 条奖励状态。
- 暂无游戏环境：先做「选牌推荐 + 原因」的规则版（可离线验证），或继续补常见卡牌效果与基线评估。

---

## 里程碑日志

| 里程碑 | 计划 | 实际 | 状态 | 备注 |
|---|---|---|---|---|
| M0 环境审计 | W0 | 合成状态已跑通；真实游戏未验证 | 部分完成 | 需安装游戏/mod |
| M1 Bridge + 回放 | W1 | bridge + capture + golden trace harness；真实回放待数据 | 部分完成 | `bridge.py` / `capture.py` / `trace.py` |
| M2 卡牌库 v0 | W2 | 87 张 Ironclad 机械字段 | 完成（待真机核对） | `data/cards/ironclad_cards.json` |
| M3 前向模型 v0 | W4 | 直接效果 + 部分能力；金标未做 | 进行中 | 下一步对拍 |
| M4 搜索 v1 | W6 | beam MVP；正式基线未做 | 进行中 | 下一步固定战斗集 |
| M5 选牌推荐 v0 | W8 | - | 未开始 | 后置 |
| M6 解释 + 集成 | W10 | 战斗理由/备选/CLI/bridge 已有 | 部分完成 | 选牌解释未做 |
| M7 评估 + 交付 | W12 | - | 未开始 | - |

---

## 实验日志模板

> 每次实验复制到 `experiments/YYYYMMDD-<slug>/notes.md`，本日志只放摘要与链接。

```markdown
# 实验：<名称>

- 日期：
- 实验 ID：
- 代码 commit：
- 游戏/mod/DB 版本：
- 配置：
- 假设：
- 变量：
- 固定条件：
- 指标：
- 样本量：
- 结果：
- 统计：
- 结论：
- 异常/失败案例：
- 下一步：
```

### 实验索引

| 日期 | 实验 ID | 主题 | 结果 | 链接 |
|---|---|---|---|---|
| - | - | - | - | - |

---

## 决策索引

| ID | 决策 | 日期 | 状态 |
|---|---|---|---|
| ADR-001 | 目标锁定 StS2 | 2026-10-21 | 已采纳 |
| ADR-002 | 混合架构，LLM 不掌战术 | 2026-10-21 | 已采纳 |
| ADR-003 | 默认 copilot | 2026-10-21 | 已采纳 |
| ADR-004 | 单角色 + 战斗 + 卡牌奖励 | 2026-10-21 | 已采纳 |
| ADR-005 | 证据约束 + 校验解释 | 2026-10-21 | 已采纳 |
| ADR-006 | 不再分发上游 mod | 2026-10-21 | 已采纳 |
| ADR-007 | 近似最优而非最优 | 2026-10-21 | 已采纳 |

---

## 版本与环境记录

> 安装后填写；每次升级追加一行，不覆盖旧记录。

| 日期 | 游戏版本 | BaseLib | Communication_Mod_STS2 | Python | OS | 备注 |
|---|---|---|---|---|---|---|
| 2026-10-21 | 待填 | 待填 | 待填 | 待填 | 宿主机 macOS（游戏/mod 兼容性待验证） | 仅文档阶段 |

---

## 变更记录

| 日期 | 版本 | 变更 |
|---|---|---|
| 2026-10-21 | v0.1 | 建立日志；完成调研与三份文档初稿；记录 7 条 ADR；完成链接/引用/表格一致性校验 |
| 2026-10-21 | v0.2 | 战斗建议 MVP：87 张卡牌库、适配/规则/搜索/建议/CLI/bridge、28 个测试；新增 ADR-008–012 |
| 2026-10-22 | v0.3 | 真实接入工具链：inspect/capture/trace、docs/04、合成 trace；测试 40；新增 ADR-013–015 |
| 2026-10-22 09:46 | v0.4 | 明确项目为 Slay the Spire 2；日志加时间线/时区/记录规则；删除 StS1 附录与工具引用；测试 41 |

---

## 参考来源

[^1]: Epoch AI, *SpireClimb*, https://epoch.ai/benchmarks/spireclimb （访问于 2026-10-21）
[^2]: Manuelbbl, *Communication_Mod_STS2*, https://github.com/Manuelbbl/Communication_Mod_STS2
[^3]: Ou, B., & Chen, S., *AI systems are improving rapidly at Slay the Spire and its sequel*, Epoch AI, 2026-10-14, https://epoch.ai/publications/ai-systems-are-improving-rapidly-at-slay-the-spire-and-its-sequel
[^4]: Lowe, S. C., & Keemink, S. W., *SpireBench: Evaluating Long-Horizon Strategic Play of Agentic Models with Slay the Spire*, arXiv:2610.12596, https://arxiv.org/abs/2610.12596
[^5]: Cheng, X., et al., *AgenticSTS: A Bounded-Memory Testbed for Long-Horizon LLM Agents*, arXiv:2607.02255, https://arxiv.org/abs/2607.02255
[^6]: brmarcosbr, *sts2-mcp-bridge*, https://github.com/brmarcosbr/sts2-mcp-bridge
[^7]: Park, D., et al., *Orak: A Foundational Benchmark for Training and Evaluating LLM Agents on Diverse Video Games*, arXiv:2506.03610, https://arxiv.org/abs/2506.03610
[^8]: Bateni, B., & Whitehead, J., *Language-Driven Play: Large Language Models as Game-Playing Agents in Slay the Spire*, ACM, 2024, https://doi.org/10.1145/3649921.3650013
