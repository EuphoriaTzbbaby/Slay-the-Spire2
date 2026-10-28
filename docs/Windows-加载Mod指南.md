# 在 Windows 上加载尖塔 AI 陪玩 Mod

面向：负责 Windows 侧验证的同学。
范围：把本仓库的 `SpireCompanion`（尖塔 AI 陪玩（开发版））装进《杀戮尖塔 2》并确认加载成功。

> **必须同时安装 CombatSolver 和 RitsuLib**：本 Mod 只负责局外自动与聊天，战斗出牌完全交给 CombatSolver；不装它们，进入战斗后没有任何自动操作。两者都从 Steam 创意工坊订阅（RitsuLib 是 CombatSolver 的依赖），装完后在游戏 Mod 列表里确认已启用。

本文所有「做对了的现象」都对应真实日志或文件，看不到时按第 4 节回传信息，不要凭感觉判断成功。

## 0. 先分清三个路径

| 用途 | Windows 路径 |
| --- | --- |
| 编译用游戏数据 | `...\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64`（必须含 `sts2.dll`、`GodotSharp.dll`） |
| Mod 加载目录 | `...\steamapps\common\Slay the Spire 2\mods\`（游戏根目录下，不是数据子目录） |
| 战斗求解器 | **CombatSolver**（创意工坊订阅）+ **RitsuLib**（CombatSolver 的依赖） |
| 游戏用户数据 | `%APPDATA%\SlayTheSpire2\spire-companion\`（档案、导出；导出时面板会显示完整路径，不必背） |

macOS 的 mods 在 app 包内，Windows 在游戏根目录，这一点两边不同，最容易装错。

> 2026-10-28：本指南已在 Windows 实机走通（安装、加载、窗口可用）。

## 1. 三种装法，选一种

### 路线 0：一键安装包（推荐给不写代码的同学）

1. 从组里拿 `SpireCompanion-win64.zip`（仓库里执行 `python3 scripts/package_release.py` 生成，`dist/` 下）；
2. 解压到任意目录（不要直接在压缩包里双击）；
3. 双击 **`install.bat`**；
4. 脚本自动找 Steam 游戏目录；找不到会让你粘贴，粘贴含 `SlayTheSpire2.exe` 的文件夹；
5. 脚本把 `SpireCompanion` 复制到 `mods\`，并检查 `CombatSolver` 和 `RitsuLib`，缺了会提示去创意工坊搜索；
6. 完全退出游戏再启动，确认 Mod 列表里「尖塔 AI 陪玩（开发版）」已启用。

说明：

- 脚本只写 `mods\SpireCompanion\`，不碰存档、不碰其他 Mod；
- 自动战斗仍然要自己订阅 **CombatSolver + RitsuLib**，脚本不能代替订阅；
- 一键脚本是 2026-10-28 新增的，**还没在 Windows 实测**；第一次用如果失败，按第 4 节把窗口截图和日志发回来。

### 路线 A：直接复制现成 DLL（最快，要求版本一致）

1. 从组里拿到三个文件：`SpireCompanion.dll`、`SpireCompanion.json`、`LICENSE`；
2. 在 `...\Slay the Spire 2\mods\` 下新建 `SpireCompanion` 文件夹，把三个文件放进去；
3. **完全退出游戏**再启动。

适用场景：只想快速看效果。注意 DLL 是针对编译机版本的游戏程序集编译的，**游戏版本不一致可能加载失败**；跨版本验证请走路线 B。

### 路线 B：从源码构建（推荐）

前置：.NET 9 SDK、Python 3.11+、git，游戏已安装。

```powershell
git clone https://github.com/EuphoriaTzbbaby/Slay-the-Spire2.git
cd Slay-the-Spire2

python scripts/game_dev.py build --game-data "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64"
python scripts/game_dev.py check
python scripts/game_dev.py install --mods-dir "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/mods"
```

也可以复制 `game/legacy/local.props.example` 为 `game/legacy/local.props`，把 `STS2GameDataDir` 改成自己的路径，之后 `build` 不用再带 `--game-data`。`local.props` 不进版本库。

说明：

- `install` 要求 `mods` 目录已存在（脚本不会替你创建游戏目录）；Windows 上必须用 `--mods-dir` 指定。
- 安装只写 `mods\SpireCompanion\` 这一处，覆盖前把旧版本备份到仓库 `.local/mod-backups/`，**不碰存档、不碰其他 Mod**。
- 游戏运行中也可以构建和安装，但**新 DLL 只在下次启动生效**。

## 2. 每一步「做对了」的现象

### 2.1 构建

控制台输出：

```
已成功生成。
    0 个警告
    0 个错误
PASS Mod contains tested core types and has no external SpireCompanion.Core assembly reference.
```

产物：`game\legacy\SpireCompanion\bin\Release\net9.0\SpireCompanion.dll`

看到 `找不到 sts2.dll` 之类的报错 → `--game-data` 指错了，应该指到 `data_sts2_windows_x86_64`，不是游戏根目录。

### 2.2 检查

```
29 checks passed. No game process or saves accessed.
```

数字不必死记，关键是没有 FAIL。

### 2.3 安装

```
Installed development mod in ...\Slay the Spire 2\mods\SpireCompanion
```

目录里应当**只有三个文件**：

```
SpireCompanion.dll
SpireCompanion.json
LICENSE
```

如果看到 `SpireCompanion.Core.dll`，说明拿到的是旧版本，删掉它——现在主 Mod 是单 DLL，多一个旧核心文件反而会导致加载失败。

### 2.4 启动游戏

Mod 列表里出现「尖塔 AI 陪玩（开发版）」，ID 为 `SpireCompanion`，并处于启用状态。

日志（`%APPDATA%\SlayTheSpire2\logs\godot.log`，找不到时看启动器控制台）应出现这三行：

```
[SpireCompanion] 初始化：只读建议模式，F9 显示/隐藏窗口。
[SpireCompanion] PANEL_READY scene=... font_ready=True fingerprint=...
[SpireCompanion] PANEL_LAYOUT ... tabs=3 titles=聊天|设置|游戏状态 ...
```

三条各代表：入口跑了、面板建出来了且拿到中文字体、窗口排版完成。**缺第一条 = Mod 没加载；缺第二条 = 面板构造失败；缺第三条 = 排版没跑完。** 有 `font_ready=False` 就是字体没拿到。

日志里出现 `WARN ... does not declare min game version` 属正常（本 Mod 未声明最低版本），不影响加载。

### 2.5 看到窗口

- 左上角有「AI 搭子」按钮（带奶龙头像），按 `F9` 或点它开窗；
- 三个页签：**聊天 / 设置 / 游戏状态**；
- 中文正常显示（不是方框）、奶油黄底色、白色圆角卡片；
- 标题右侧显示「只读建议」，不是「就绪 / 暂停」。

### 2.6 功能自检（各 10 秒）

| 做什么 | 做对了的现象 |
| --- | --- |
| 进战斗，看「游戏状态」 | 血量、金币、手牌、敌人与实际一致 |
| 「设置」填地址/模型/Key → 保存设置 | 提示「设置已保存，API Key 已加密写入当前本地档案」 |
| 「聊天」发一句话 | 有中文回复，加粗/列表按样式显示，不出现 `**` 原文 |
| 事件房/卡牌三选一/商店/篝火/地图各问一句 | 能说出真实选项、价格或可达节点 |
| 「调试」点导出 | 面板显示导出文件的完整路径；文件是 JSON |
| 完全退出再启动 | 设置页的 Key 显示为圆点（档案自动解锁成功） |

## 3. 常见现象对照

| 现象 | 判断 | 处理 |
| --- | --- | --- |
| 没有「AI 搭子」按钮 | Mod 没加载 | 完全退出游戏 → 确认 `mods\SpireCompanion\` 三个文件 → 确认 Mod 列表已启用 → 再看日志第一条 |
| 中文显示成方框 | 用了旧 DLL 或字体未就绪 | 确认 DLL 是本次构建；把 `PANEL_READY` 那行发回 |
| 窗口开了但内容空白 | 状态读取失败 | 日志里找 `[SpireCompanion] read_state` |
| 「交给 CombatSolver」是灰的 | 没装 CombatSolver / RitsuLib，或不在战斗 | **这不是正常状态**：去创意工坊订阅 CombatSolver 与 RitsuLib，重启后确认加载 |
| 保存设置提示「未落盘」 | 本地档案没解锁 | 先创建/解锁档案；只想临时用就取消勾选「记住」 |
| 保存密钥报错 | Windows 凭据管理器路径**未实测** | 先取消勾选「记住」用会话密钥，把报错原文发回 |
| 聊天 401/403/404/429 | 密钥、权限、地址或额度 | 按提示核对；401 是 Key，404 是地址/模型名 |
| 聊天空回复 | 推理型模型或输出被截断 | 换 `deepseek-chat` / `deepseek-flash`；按提示区分 |
| 游戏崩溃或卡死 | — | 关游戏，删除 `mods\SpireCompanion\` 回退；旧版本在仓库 `.local/mod-backups/` |

## 4. 反馈问题时给什么

1. 日志中**所有含 `[SpireCompanion]` 的行**（整段复制即可）；
2. 现象截图（**不要包含 Key 明文**）；
3. 使用的模型名与地址（**不要贴 Key**）；
4. 如果导出过状态 JSON，可以直接附上——它不包含密钥。

有这四样，大多数问题不用再问第二轮。

## 5. 边界：这些「没做到」是已知的，不算 bug

- 本 Mod **不自己出牌**；自动战斗由 **CombatSolver** 负责，**必须安装 CombatSolver + RitsuLib**，否则战斗不会自动进行。
- 奖励、商店、休息点、地图**只读**，不会代替你选择或购买。
- 语音尚未接入。
- Windows 的密钥缓存路径（凭据管理器）还没实测，macOS 已通过。
- 主验证平台是 macOS；Windows 的问题按第 4 节格式回传，由游戏组统一记录。

## 6. 快速命令汇总

```powershell
# 构建（指定 Windows 游戏数据目录）
python scripts/game_dev.py build --game-data "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64"

# 无游戏环境检查
python scripts/game_dev.py check

# 安装到游戏 mods
python scripts/game_dev.py install --mods-dir "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/mods"

# 构建 + 安装（不自动启动游戏；macOS 才有自动启动）
python scripts/game_dev.py run --no-launch --game-data "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64" --mods-dir "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/mods"
```
