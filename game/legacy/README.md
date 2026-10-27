# 游戏主代码：SpireCompanion

C# / .NET 9 单人 Mod 的正式开发目录。第一批实现状态查看、控制开关和手动结束回合，已加入 CombatSolver 状态与接管/停止桥接代码，真实战斗集成尚待验证；已加入在线模型文字聊天与只读状态建议；局外自动托管仍待开发。

| 目录 | 用途 |
| --- | --- |
| SpireCompanion | 游戏 Mod 入口、状态读取和开发面板。 |
| SpireCompanion.Core | 不依赖游戏的核心源码；主 Mod 直接编入这些源码，检查工程单独引用。 |
| SpireCompanion.Checks | 不启动游戏的控制语义检查。 |

## 构建和检查

从仓库根目录执行：

```bash
python scripts/game_dev.py build
python scripts/game_dev.py check
```

macOS 自动检测标准 Steam 游戏数据路径。Windows 构建需指定游戏数据目录，或复制 local.props.example 为 local.props 并修改：

```powershell
python scripts/game_dev.py build --game-data "C:/Program Files (x86)/Steam/steamapps/common/Slay the Spire 2/data_sts2_windows_x86_64"
```

本机路径和游戏程序集不提交。Windows 编译和运行尚待该平台验证。

## 安装与手动验收

先构建，再执行安装，macOS 标准路径可自动定位：

```bash
python scripts/game_dev.py install
```

其他安装位置使用 `--mods-dir` 指定已存在的 mods 目录。仅复制主 Mod DLL、manifest 和许可证；已有版本先备份到仓库 .local/，不改存档或其他 Mod，不自动启动游戏。

重新启动游戏、启用“尖塔 AI 陪玩（开发版）”，按 F9 显示或隐藏面板。默认暂停；手动操作需要主动启用。本阶段存在 SpireBuddy、STS2AIAgent 或 CombatSolver 程序集时禁用本模块的手动结束回合。真实战斗中可通过“交给 CombatSolver 打本场战斗”按钮接管求解器，但同时存在 Buddy 或 STS2-Agent 时仍拒绝接管，避免双重控制。

验收顺序：主菜单状态 → 铁甲战士战斗中的血量/金币/手牌 → 启用手动操作 → 点击一次结束回合 → 暂停/状态变化后不继续动作。其他角色、多人、动画、选牌和覆盖界面期间应拒绝操作。

点击暂停会停止本模块接管的求解器，采用不保存到设置文件的禁用方式取消搜索与部署；只有再次点击接管才恢复本模块禁用的求解器。不会自动启用原本由玩家禁用的求解器。桥接兼容检查失败时停止操作并报告。

可运行 `python scripts/game_smoke.py` 进行 macOS 独立离线加载测试，不使用真实跑局或 Steam 存档。它会在日志中读取一次 `PANEL_LAYOUT`，校验窗口位于视口内、三个页签齐全、滚动区确实撑开；仍不证明可见字体、战斗操作或真实求解器集成。

本模块原生动作仍是手动单次提交，只有求解器接管按钮请求自动战斗。手动接管和完整 UI 效果需实机验证；控制检查通过不能代替真实游戏验收。

开发规划和进度见 [规划](../../docs/单人版游戏模块开发规划.md)、[进度](../../docs/单人版游戏模块开发进度.md)。

## 状态调试

面板展示能量、格挡、敌人生命及牌组/遗物/药水数量，点击“导出当前状态（调试）”会读取新快照并写入 `user://spire-companion/captures/`。文件位置记录在游戏日志中；导出不执行游戏动作，不包含隐藏抽牌顺序或模型密钥。

独立控制检查现有 26 项。完整战斗状态、实际按钮操作和真实 CombatSolver 接管仍需实机验收。运行记录见 [桥接开发记录](../../docs/游戏模块求解器桥接开发记录.md)。

## 文字聊天与当前状态建议

“设置”页签内填写 Chat Completions 服务地址、模型名和会话 API Key，“聊天”页签发送消息或点击“分析当前局面”。支持保存不含密钥的配置、取消请求、清空对话和三档回复风格。勾选记住后点击保存，密钥存入系统钥匙串或凭据管理器，重启自动读取；不写入项目配置文件，也可由 SPIRE_COMPANION_API_KEY 环境变量提供。

只有主动发送才会调用模型；回复不会执行游戏动作。当前快照尚未包含完整奖励、商店和地图选项，信息不足时应由搭子说明。真实服务连通性和界面交互尚待联调。本轮未启动游戏。

详细说明见 [文字聊天开发记录](../../docs/游戏模块文字聊天开发记录.md)。

手动验证入口见 [游戏内窗口与 API 手动验证](../../docs/游戏内窗口与API手动验证.md)：重启启用 Mod，打开“聊天与 API”，填写配置并点击“测试 API 连接”，成功后可以直接聊天。

加载修复：主 Mod 现为单 DLL，不再依赖旁置 SpireCompanion.Core.dll，避免游戏在初始化前扫描类型时无法解析依赖。安装脚本会备份旧安装并移除旧核心 DLL；更新后必须完全重启游戏。

默认服务地址为 https://api.deepseek.com，默认模型为 deepseek-flash。保存设置可记住密钥，删除按钮可清除。macOS 钥匙串读写删除已通过本机检查；Windows 凭据存储路径尚待 Windows 实测。

API Key 当前采用 [跨平台加密文件与本地账户档案](../../docs/APIKey本地加密存储与账户档案.md)。系统安全存储仅用于自动解锁缓存；云端登录以后接入现有 MySQL。以上历史说明中“API Key 直接存入系统凭据”的行为已由新方案替代。
