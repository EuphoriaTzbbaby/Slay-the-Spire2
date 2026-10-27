# 尖塔 AI 陪玩

《杀戮尖塔 2》单人托管与双人陪玩项目。游戏端使用 C#，语音服务使用 Python，通过本机 WebSocket 通信。

## 目录与分工

```text
.
├── game/                  # 游戏主代码与上游子模块
│   ├── spire-buddy/        # 聊天与自动操作基础（子模块）
│   ├── STS2-Agent/         # 双开联机与接口基础（子模块）
│   ├── CombatSolver/      # 战斗求解基础（子模块）
│   └── legacy/            # 后续 C# 游戏主代码开发目录
├── voice/                 # Python 语音服务开发入口
├── protocol/              # 通信协议、Schema 和消息示例
├── docs/                  # 需求、协作说明、历史日志与汇报材料
├── scripts/               # 仓库检查和联调脚本
└── .github/               # 自动检查与 PR 模板
```

## 获取完整源码

```bash
git clone --recurse-submodules https://github.com/EuphoriaTzbbaby/Slay-the-Spire2.git
cd Slay-the-Spire2
```

已有克隆更新后执行：

```bash
git submodule sync --recursive
git submodule update --init --recursive
```

子模块锁定到主仓库记录的提交，不要使用 `--remote` 随意更新。三个上游的修改应先提交到团队可访问的 fork，再更新主仓库的 URL 和提交引用，避免成员无法获取未发布的本地修改。

## 当前状态

已有三个上游项目；C# 主工程位于 game/legacy/，已完成状态面板、手动结束回合、暂停控制、状态导出、CombatSolver 接管桥接和在线模型文字聊天代码；编译及独立控制检查通过，真实战斗集成尚待验收。自动托管、语音服务和多人 CombatSolver 适配尚待实现。

## 验证

```bash
python scripts/check_repository.py
python scripts/game_dev.py check
```

检查需要 Python 3.11+，控制检查另需 .NET 9 SDK；不启动游戏或采集麦克风。构建和安装主 Mod 见 [主工程说明](game/legacy/README.md)。主工程编译需要本机游戏程序集。

## 文档

- [项目环境与资源](docs/项目环境与资源.md)：已有云服务器和 MySQL，后续接入云端登录与同步。
- [API Key 本地加密与账户档案](docs/APIKey本地加密存储与账户档案.md)：跨平台文件、解锁密码和本机自动解锁。

- [项目分工（暂定）](docs/项目分工.md)：马同学、胡同学负责语音，其余三人负责游戏。

- [单人版需求](docs/AI陪玩搭子-简版需求分析.md)
- [单人版游戏模块开发规划](docs/单人版游戏模块开发规划.md)：复用方案、模块职责、开发阶段与验收安排。
- [单人版游戏模块开发进度](docs/单人版游戏模块开发进度.md)：完成标 ✓，未完成或未验证标 ✗。
- [双人版需求](docs/AI双排陪玩-简版需求分析.md)
- [语音开发与 API](docs/语音模块-开发功能与API.md)：Windows 优先。
- [GitHub 团队协作](docs/GitHub团队协作与主分支保护.md)
- [Github代码提交指南](docs/Github代码提交指南.md)：创建分支、保存提交、上传代码及 PR 流程（以语音模块为例）。
- [游戏内窗口与 API 手动验证](docs/游戏内窗口与API手动验证.md)：加载 Mod、测试密钥和文字聊天。
- [待办：AI 局势感知与开发迭代](docs/待办-AI局势感知与开发迭代.md)：各界面快照、提示词、卡牌文本查询，以及改代码必须重启的应对方案。
- [文字聊天使用与开发记录](docs/游戏模块文字聊天开发记录.md)
- [游戏开发入口](game/README.md)
- [语音开发入口](voice/README.md)
- [协议入口](protocol/README.md)

API 密钥、个人配置、录音、模型权重和构建产物不进入源码仓库。上游项目保留各自许可证与历史，根目录许可证不替代第三方许可。
