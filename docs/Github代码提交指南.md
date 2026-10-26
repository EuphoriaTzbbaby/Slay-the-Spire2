# Github代码提交指南

本文面向项目全体开发成员，以开发语音唤醒功能为例，适用于 Windows 上的 PowerShell 或 Git Bash。请先安装 Git，并接受项目负责人的 GitHub 仓库协作邀请。

代码保存在电脑上不等于已上传 GitHub：`commit` 保存本地版本，`push` 上传远端分支，PR 用于申请合并进主分支。

## 1. 第一次下载项目

在准备存放项目的目录打开终端，执行：

```bash
git clone --recurse-submodules https://github.com/EuphoriaTzbbaby/Slay-the-Spire2.git
cd Slay-the-Spire2
git remote -v
```

- `clone` 下载主仓库；`--recurse-submodules` 同时获取游戏端的三个上游项目。
- `cd` 进入项目根目录，后面的命令默认都在这里执行。
- `remote -v` 确认 origin 指向团队主仓库。

如果已经下载过，不要反复 clone，直接进入原项目目录。

首次提交前配置身份，下面两行替换为自己的信息：

```bash
git config user.name "你的名字"
git config user.email "你的GitHub邮箱"
```

这些是提交作者信息，不是登录凭据。邮箱可以使用 GitHub Settings → Emails 中提供的隐私邮箱。

首次推送可能需要登录 GitHub。使用 Git 凭据管理器的浏览器登录提示，不要在命令或代码中填写账号密码和访问令牌。

## 2. 开始一个新任务

### 为什么要创建分支

可以把 `main` 理解为团队已经确认的正式代码，把任务分支理解为自己本次开发的独立工作线。

例如，你开发语音唤醒，另一位成员开发游戏交接：

```text
main：团队已审核的代码
├── feat/voice-wakeup：你开发语音唤醒
└── feat/game-handoff：队友开发游戏交接
```

创建分支的原因：

- 未完成、存在问题的代码可以先保存在自己的分支，不影响 main。
- 成员可以分别开发，再通过 PR 审查各自的修改。
- main 已受保护，普通成员不能直接推送，任务分支是交付代码的入口。
- 某项功能暂时不采用时，可以不合并该分支，main 不受影响。

分支不是另一份文件夹，也不会自动复制一个项目目录。切换分支时，Git 会让当前目录显示对应分支的已提交内容。不要误以为分支是权限隔离：成员仍可能有权限修改其他普通分支，main 的限制来自 GitHub 规则。

### 在哪里、怎么创建分支

推荐在自己电脑上创建，再推送到 GitHub。打开项目根目录 `Slay-the-Spire2` 的终端；如果在 VS Code 中打开该项目，可以使用“终端 → 新建终端”。

下面三条命令表示：先回到 main，更新团队代码，然后以它为起点创建语音任务分支。执行前先按下文检查工作区。

先检查工作区：

```bash
git status
```

确认没有未保存到 Git 的修改后执行：

```bash
git switch main
git pull --ff-only origin main
git switch -c feat/voice-wakeup
```

| 命令 | 用途 |
| --- | --- |
| `git switch main` | 切换到本地主分支。 |
| `git pull --ff-only origin main` | 获取团队已合并的新代码；出现分叉时停止，避免意外合并。 |
| `git switch -c feat/voice-wakeup` | 创建并切换到本次任务分支，避免直接修改 main。 |

如果 status 显示有未提交改动，先按下面的提交步骤处理，不要覆盖或丢弃。main 更新失败时保留现场，请负责人协助，不要执行强制重置。

创建成功后执行：

```bash
git branch --show-current
```

如果输出 `feat/voice-wakeup`，说明当前已经处于语音任务分支。此时可以开始修改 `voice/` 下的代码。

注意：`git switch -c` 只在电脑上创建分支，直到后文执行 `git push -u origin feat/voice-wakeup` 后，GitHub 上才会出现这个分支。

分支名可以按任务变化，例如：

- `feat/voice-wakeup`：语音唤醒。
- `feat/voice-recognition`：语音识别。
- `feat/voice-playback`：语音播报。
- `fix/voice-reconnect`：连接修复。

同一个任务后续继续开发时，使用 `git switch feat/voice-wakeup`，不要再次用 `-c` 创建。不同成员使用不同任务分支，避免同时推送同名分支。

## 3. 在 voice 目录开发

语音代码放入 `voice/`，建议结构如下，示例文件需要在实际开发中创建：

```text
voice/
├── README.md
├── pyproject.toml
├── src/
│   └── spire_voice/
│       └── wakeup.py
└── tests/
    └── test_wakeup.py
```

通信接口以 `protocol/` 和 `docs/语音模块-开发功能与API.md` 为依据。改动协议时，同步修改文档、示例和对应验证，并通知游戏端负责人。

不要上传：API 密钥、访问令牌、个人配置、虚拟环境、录音、模型权重、临时日志和打包产物。不要直接修改或提交 `game/` 下的子模块引用，除非任务明确需要。

## 4. 保存一个本地版本

完成一小段功能后，先运行与改动相关的验证，再检查：

```bash
git status
git diff
```

选择本次任务的具体文件暂存，以下路径只是示例，替换为实际文件：

```bash
git add voice/src/spire_voice/wakeup.py voice/tests/test_wakeup.py
git diff --cached
git commit -m "feat: 实现中文语音唤醒"
```

| 命令 | 用途 |
| --- | --- |
| `git diff` | 查看尚未暂存的修改。 |
| `git add` | 选择这一次要提交的文件，也包含指定文件的删除记录。 |
| `git diff --cached` | 确认本次提交实际包含的内容，防止夹带其他修改。 |
| `git commit` | 在自己电脑上创建版本记录，此时还没有上传 GitHub。 |

避免直接使用 `git add .`，尤其是工作区存在其他人的改动或无关文件时。

## 5. 上传到 GitHub

第一次推送任务分支：

```bash
git push -u origin feat/voice-wakeup
```

这会上传提交并建立远端分支关联。打开 GitHub 仓库，切换到 `feat/voice-wakeup`，即可查看刚上传的代码；main 此时不会变化。

同一个分支后续修改：

```bash
git add voice/src/spire_voice/wakeup.py
git diff --cached
git commit -m "fix: 修复重复唤醒触发"
git push
```

每天结束工作前，建议把有意义的小批修改 commit 并 push。未完成但需要备份的工作也可以推送到自己的分支，使用 Draft PR 说明进度，不要申请作为已完成功能合并。

## 6. 功能交付时创建 PR，请负责人合并

日常向自己的任务分支提交和推送代码不需要 PR，也不需要负责人批准。一个可验收的功能准备合并进 main 时再创建一个 PR；后续向同一分支推送会自动更新该 PR，不必每次新建。

进入 GitHub 仓库 → Pull requests → New pull request：

- base：`main`。
- compare：`feat/voice-wakeup`。
- 未完成时选择 Draft；准备好审查时转为 Ready for review。
- 请求项目负责人审查。

PR 示例：

```text
标题：实现中文语音唤醒

解决的问题：
支持检测默认唤醒词“搭子”。

修改内容：
新增唤醒检测和重复触发抑制。

验证：
填写实际运行的命令、设备和结果。
未测试的项目明确写出，不把计划写成通过。

接口影响：
是否修改 wake.detected 事件或协议字段。

关联任务：
Closes #实际Issue编号
```

PR 是合并申请，不是再次上传代码。代码在 push 后已经保存在 GitHub；只有负责人合并 PR 后才进入 main。

收到修改意见后，继续在同一任务分支提交并 push，原 PR 会自动更新，不需要重建 PR。

## 7. PR 合并后同步代码

确认工作区干净、PR 已合并后执行：

```bash
git switch main
git pull --ff-only origin main
git submodule update --init --recursive
git fetch --prune
```

这会获取主仓库新代码、同步锁定的上游版本，并清理已经删除的远端分支引用。子模块内有本地改动时，先处理改动，不要强制更新。

本地任务分支可以稍后清理：

```bash
git branch -d feat/voice-wakeup
```

如果提示尚未合并，可能是负责人使用了 Squash merge。先确认改动确实已进入 main，再请负责人协助处理；不要直接强制删除。

下一个任务从最新 main 创建新分支，不在已经合并的旧任务分支继续堆代码。

## 8. 常见情况

| 情况 | 处理方法 |
| --- | --- |
| commit 后 GitHub 没变化 | 还需要 push，并在网页切换到自己的任务分支。 |
| push 成功但 main 没变化 | 正常，需要提交 PR 并由负责人合并。 |
| 向 main 推送被拒绝 | 主分支保护正常生效，改用任务分支和 PR。 |
| 提示没有写权限 | 确认接受邀请、登录的是自己的正确账号，联系负责人检查权限。 |
| push 提示 non-fast-forward | 远端有本地没有的提交，先 fetch 并确认来源，再协商合并；不要强制推送。 |
| 出现冲突 | 不要随意整段覆盖对方代码，确认双方意图并解决，不确定就联系负责人。 |
| 发现密钥已上传 | 立即撤销或轮换密钥并通知负责人；仅删除文件不能清除历史中的密钥。 |

## 9. 最短操作清单

```text
开始任务：检查状态 → 更新 main → 创建任务分支
开发过程：写代码 → 验证 → 检查差异 → add → commit → push
交付功能：创建 PR → 根据审查修改 → 负责人合并
下个任务：同步 main → 创建新任务分支
```
