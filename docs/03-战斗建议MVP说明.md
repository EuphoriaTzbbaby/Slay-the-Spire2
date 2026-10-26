# 战斗建议 MVP 说明

> 实现日期：2026-10-21；最近更新：2026-10-22 09:46
> 状态：离线 + 合成状态 bridge 已跑通；真实游戏联调待做。
> 关联：[README](../README.md)、[开题分析](01-开题分析.md)、[项目规划](02-项目规划.md)、[日志](LOG.md)

## 1. 目标与边界

本 MVP 只做**战斗内建议**：

```text
战斗状态 JSON
  -> 状态解析
  -> 合法动作枚举
  -> 前向模型模拟
  -> beam 搜索 + 启发式评估
  -> 推荐动作 + 理由 + 备选 + 风险提示
```

不做：选牌推荐、卡牌奖励自动选择、地图/商店/事件、长程规划、全角色、药水/遗物完整建模。

## 2. 代码结构

| 文件 | 职责 |
|---|---|
| `src/spirepilot/model.py` | 卡牌、玩家、敌人、动作、战斗状态的数据模型 |
| `src/spirepilot/cards.py` | 卡牌库加载、`CardCatalog` 回退、卡牌 ID 规范化 |
| `src/spirepilot/adapter.py` | 解析 `Communication_Mod_STS2` 的 PascalCase JSON |
| `src/spirepilot/rules.py` | 合法动作、伤害/格挡/抽牌/能量/状态、敌方回合 |
| `src/spirepilot/evaluate.py` | 局面评分（血量、敌方血量/格挡、能力、能量） |
| `src/spirepilot/search.py` | 限定预算 beam 搜索、抽牌顺序采样、根动作聚合 |
| `src/spirepilot/advisor.py` | 组织建议、复算推荐连招、生成理由与备选 |
| `src/spirepilot/cli.py` | `doctor` / `demo` / `advise` / `serve` / `live` / `overlay` / `rollout` / `inspect` / `capture` / `trace` |
| `src/spirepilot/bridge.py` | 本地 HTTP server；copilot 默认回 `OK`，`--auto` 回动作 |
| `src/spirepilot/inspect.py` | 真实状态字段审计（Block、费用、Catalog 覆盖、索引一致性） |
| `src/spirepilot/capture.py` | 只记录原始状态的 HTTP capture，供 D1–D3 使用 |
| `src/spirepilot/trace.py` | Golden trace 回放与期望状态 diff |
| `src/spirepilot/overlay.py` | Phase A 置顶建议面板（Tkinter，轮询 15526） |
| `src/spirepilot/rollout.py` | 2–3 回合 rollout，用 beam 策略补齐当前/后续回合 |
| `src/spirepilot/sts2mcp.py` | STS2MCP (15526) 状态 adapter 与实时 `live` 数据源 |
| `data/cards/ironclad_cards.json` | 87 张 Ironclad 卡的机械字段快照 |
| `data/samples/combat_basic.json` | 双敌人、5 张手牌的合成状态 |
| `data/samples/combat_minimal.json` | 模拟真实上游缺 Block/牌堆字段的最小状态 |
| `scripts/import_spire_codex.py` | 从 Spire Codex 公共 API 重新导入卡牌 |

## 3. 数据流

### 离线

```bash
PYTHONPATH=src python3 -m spirepilot advise \
  --state data/samples/combat_basic.json --json
```

### 在线（默认 copilot）

```text
Game (Communication_Mod_STS2)
  --POST /update_state--> SpirePilot bridge
                          -> 解析/建议/打印
  <------ "OK" ---------- 不执行动作
```

### 在线（自动战斗）

```text
  <-- {"Type":"PlayCard","HandIndex":2,"TargetIndex":0} --
```

注意事项：

- bridge 必须占用 `127.0.0.1:5000`，不要同时跑上游 `server.py`。
- 游戏把响应体直接当命令解析；不能包 `{"status":"OK","action":...}`。
- 每次只回一个动作；手牌索引在出牌后会变，必须等下一次状态。
- 默认 copilot；`--auto` 需要显式开启。

## 4. 搜索与评估

> 当前没有训练机器学习模型；“模型”是手写前向模拟器 + 手调评分 + 搜索。

### 4.1 两种求解器

| 模式 | 说明 | 用途 |
|---|---|---|
| `beam` | 限定宽度/深度的 beam search | 快速近似，适合大局面/回退 |
| `exact` | 当前回合穷举 DFS，按「胜利/存活 → 实际掉血 → 敌方 HP → 动作数」排序 | 单回合模型内最优、验证 beam |

`live` 默认 `--solver exact`；`--solver compare` 同屏对比 exact/beam，并附带 2T rollout。`--solver rollout` 直接看首动作的 2 回合排序。

### 4.2 2–3 回合 rollout

`rollout` 用当前首动作 + 小 beam 策略补完整回合，再结算敌方、抽下一手，重复 2–3 回合，比较最终 HP/敌 HP。它用于发现“当前回合好、下一回合崩”的短视线路。

```bash
PYTHONPATH=src python3 -m spirepilot rollout --state data/samples/sts2mcp_combat.json --turns 2
```

当前只作为诊断/对照，尚未替换 `live` 的 exact 决策。

### 4.3 当前回合 exact 的边界

- 只对**一个抽牌样本**穷举；`--samples` 可增加样本，但结论仍是「样本内最优」。
- 不搜索整场战斗；敌人回合/后续抽牌用启发式评估。
- 未建模遗物/药水/全部能力；所以是**模型内最优**，不是游戏全局最优。


- 动作：手牌 × 合法目标，加 `EndTurn`。
- 前向模型：直接伤害、格挡、抽牌、能量、易伤/虚弱/力量、部分能力、敌方可见攻击意图。
- 抽牌：对抽牌堆做确定性采样（默认 2 个样本），分别搜索后聚合根动作得分。
- 搜索：beam，默认深度 5、宽度 18、650ms/步；超时保留当前最优。
- 评估：玩家剩余 HP、敌方 HP/格挡、能力价值、剩余能量惩罚；清场有巨大奖励。
- 输出：首选动作、推荐连招、理由、备选评分、近似警告。

## 5. 已验证内容

- `doctor`：87 张卡、示例状态、建议链路通过。
- `demo`：示例状态推荐 Thunderclap（AOE），并给出连招、敌方回合后 HP、备选。
- 单元/集成测试：54 个，覆盖：
  - 卡牌库/别名/`CardCatalog` 回退
  - 状态解析、缺失格挡警告
  - 合法动作、Bash/Thunderclap/Defend
  - Body Slam、Whirlwind、Offering、Fiend Fire、Second Wind
  - 零能量结束回合、战斗结束
  - CLI JSON、HTTP copilot/auto/health
  - 状态审计、capture、golden trace、overlay view、2 回合 rollout
- 手工 HTTP 冒烟：
  - copilot 返回 `OK`
  - auto 返回 `{"Type":"PlayCard","HandIndex":2,"TargetIndex":0}`

## 6. 已知限制

1. **没有真实游戏联调**：尚未安装/启动 StS2 与 mod；状态字段按源码推断。
2. **玩家/敌人格挡字段缺失**：解析器按 0 处理并警告；这会显著影响攻防估值。应优先扩展 scanner 导出 `Block`。
3. **卡牌效果覆盖不全**：87 张卡有基础数值，但复杂能力（如 Aggression、Juggernaut 的部分交互）只做近似/估值。
4. **遗物/药水未建模**：状态中已折算的力量可用；其他遗物效果忽略。
5. **强化牌**：手牌不携带升级标记时按基础数值；`CardCatalog` 有升级信息时可部分覆盖。
6. **不是胜率结论**：当前验证是规则和集成测试，不是对局胜率评估。
7. **长程规划缺失**：只做当前回合 + 敌方回合的短视评估，不规划后续抽牌/牌组。

## 7. 下一步

1. **D1–D3 真实联调**：安装 BaseLib + `Communication_Mod_STS2`，抓 20 条状态，确认字段与命令。
2. **补 `Block` 字段**：本地扩展 scanner，或从 bridge 维护的影子状态推导；这是当前最大精度瓶颈。
3. **金标对拍**：录 10–20 条真实动作轨迹，逐步对比伤害/格挡/能量/抽牌。
4. **增加卡牌覆盖**：优先当前 Ironclad 卡池中复杂但常见的牌。
5. **做基线评估**：贪心 vs beam，固定种子/固定战斗，报告掉血/胜率/延迟。
6. **之后再接选牌推荐**：先有稳定的战斗 solver 和回放，再做卡牌奖励。
