# 尖塔 AI 陪玩

《杀戮尖塔 2》单人托管与双人陪玩项目。游戏端使用 C#，语音服务使用 Python，通过本机 WebSocket 通信。

## 目录与分工

```text
.
├── game/                  # 游戏端、上游子模块和历史原型
│   ├── spire-buddy/        # 聊天与自动操作基础（子模块）
│   ├── STS2-Agent/         # 双开联机与接口基础（子模块）
│   ├── CombatSolver/      # 战斗求解基础（子模块）
│   └── legacy/            # 旧 Python 战斗建议原型
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

已有上游代码与旧战斗原型；语音服务、统一游戏入口和多人 CombatSolver 适配尚待实现。新目录不是已完成产品。

## 验证

```bash
python scripts/check_repository.py
python scripts/test_legacy.py
```

这两条命令可在 Windows 和 macOS 使用（需要 Python 3.11+），不启动游戏、不采集麦克风。完整游戏构建需要本机合法安装的游戏程序集，参见各子模块说明。

旧原型仍可运行：

```bash
cd game/legacy
python -m pip install -e .
spirepilot doctor
```

## 文档

- [项目分工（暂定）](docs/项目分工.md)：马同学、胡同学负责语音，其余三人负责游戏。

- [单人版需求](docs/AI陪玩搭子-简版需求分析.md)
- [双人版需求](docs/AI双排陪玩-简版需求分析.md)
- [语音开发与 API](docs/语音模块-开发功能与API.md)：Windows 优先。
- [GitHub 团队协作](docs/GitHub团队协作与主分支保护.md)
- [Github代码提交指南](docs/Github代码提交指南.md)：创建分支、保存提交、上传代码及 PR 流程（以语音模块为例）。
- [游戏开发入口](game/README.md)
- [语音开发入口](voice/README.md)
- [协议入口](protocol/README.md)

API 密钥、个人配置、录音、模型权重和构建产物不进入源码仓库。上游项目保留各自许可证与历史，根目录许可证不替代第三方许可。
