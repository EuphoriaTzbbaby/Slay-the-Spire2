# GitHub 团队协作与 main 分支保护

## 1. 目的

主仓库由项目负责人维护，游戏和语音成员在自己的任务分支开发，通过 Pull Request（PR）提交修改，由负责人审查合并。

目标是让成员能够开发和推送任务分支，但不能直接修改 main，也不能自行合并到 main。分支名称本身不提供权限限制，必须在 GitHub 服务端配置规则。

本文记录配置和操作方法，不表示仓库已经完成配置或权限验证。

## 2. GitHub 网页配置

以下是网页设置，不是终端命令。

### 邀请成员

进入仓库 Settings → Collaborators，邀请成员自己的 GitHub 账号。负责人保留管理员权限，普通开发成员不要授予管理员角色，也不要共享账号或访问令牌。

原因：让每次修改可追溯到具体成员，同时保证只有负责人可以调整仓库规则。

### 创建主分支规则

进入 Settings → Rules → Rulesets → New ruleset → New branch ruleset。

| 设置 | 配置 | 为什么这样做 |
| --- | --- | --- |
| Ruleset Name | `protect-main` | 清楚标识规则用途。 |
| Enforcement status | `Active` | 确保规则生效，而不是仅保存一个未启用的配置。 |
| Target branches | Add target → Include by pattern → `main` | 精确保护 main，允许成员正常推送其他任务分支。 |
| Bypass list | 仅添加 `Repository admin` | 允许负责人更新主分支；前提是管理员只有负责人。 |
| Bypass mode | 管理员行右侧 `…` → `For pull requests only` | 负责人通过 PR 更新 main，避免直接推送绕开审查流程。 |
| Restrict updates | 勾选 | 仅允许具有绕过权限的角色更新 main，包括合并 PR。 |
| Restrict deletions | 勾选 | 阻止普通成员删除 main。 |
| Block force pushes | 勾选 | 阻止强制推送覆盖主分支历史。 |
| Require a pull request before merging | 勾选 | 明确以 PR 作为提交改动的入口。 |
| Restrict creations | 暂不勾选 | 当前目标是保护已有 main，无需额外限制创建。 |
| Require status checks to pass | 配好自动检查后再启用 | 防止未配置或无法运行的检查阻塞合并。 |

不要将 `Write`、`Maintain` 或普通成员加入绕过名单，否则可能破坏“只有负责人合并”的目标。切到 All 标签检查完整绕过名单，而不是只查看 Users 或 Roles 中的一类。

确认配置后点击页面底部 Create 或 Save changes。随后重新打开规则，检查目标、启用状态和保存结果。

注意：同一个规则集内的管理员 PR 绕过权限也可能绕过其中其他检查。后续需要强制管理员也通过审核或 CI 时，应单独设计不被该权限绕过的规则集，不要认为勾选后必然约束所有管理员合并。

### 套餐与可用性

GitHub 免费公开仓库支持规则集；私有仓库需要核对 Pro、Team 等相应套餐。若页面提示规则不执行，不能将其视作主分支已经受保护。

参考：[规则集创建说明](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/creating-rulesets-for-a-repository)、[可用规则说明](https://docs.github.com/en/repositories/configuring-branches-and-merges-in-your-repository/managing-rulesets/available-rules-for-rulesets)。

## 3. 成员首次获取项目

将下面示例中的 OWNER 和 REPO 替换为实际仓库所有者和仓库名。

```bash
git clone https://github.com/OWNER/REPO.git
cd REPO
git remote -v
```

| 命令 | 为什么执行 |
| --- | --- |
| `git clone` | 下载项目和 Git 历史到成员自己的电脑。 |
| `cd REPO` | 进入项目目录，保证后续操作作用于正确仓库。 |
| `git remote -v` | 确认 origin 指向团队主仓库，避免推送到错误位置。 |

## 4. 每个任务的开发流程

开始前先检查工作区；存在未提交改动时，先妥善提交或保存，不要覆盖改动。

```bash
git status
git switch main
git pull --ff-only origin main
git switch -c feat/voice-wakeup
```

| 命令 | 为什么执行 |
| --- | --- |
| `git status` | 检查当前分支和未提交改动。 |
| `git switch main` | 回到主分支，准备获取已合并代码。 |
| `git pull --ff-only origin main` | 更新本地 main；本地和远端分叉时停止，避免自动产生意外合并。 |
| `git switch -c feat/voice-wakeup` | 从最新 main 创建本次任务分支，隔离未完成修改。 |

分支名按任务替换，例如 `feat/game-handoff`、`feat/voice-websocket`、`fix/voice-reconnect`。不建议多人同时推送同一个任务分支。

## 5. 提交与推送

开发并完成对应验证后执行：

```bash
git status
git diff
git add voice/wakeup.py
git diff --cached
git commit -m "feat: add voice wakeup detection"
git push -u origin feat/voice-wakeup
```

`voice/wakeup.py` 只是示例，替换为本次实际修改的文件；多个文件逐项指定。

| 命令 | 为什么执行 |
| --- | --- |
| `git diff` | 检查尚未暂存的修改，发现误改内容。 |
| `git add` | 仅暂存本次任务需要交付的文件。 |
| `git diff --cached` | 检查真正将进入提交的内容。 |
| `git commit` | 创建本次修改的历史记录。 |
| `git push -u` | 上传任务分支并设置上游关联，供团队审查。 |

不要提交 API 密钥、个人配置、原始录音、游戏程序集、模型大文件或构建产物；通过 .gitignore 和提交前检查管理这些文件。

## 6. 创建 PR 与合并

在 GitHub 仓库打开 Pull requests → New pull request：

- base 选择 `main`。
- compare 选择自己的任务分支。
- 写明问题、修改内容、验证方式、未完成事项，关联对应 Issue。
- 请求负责人审查；需要提前讨论时可以先创建 Draft PR。

成员根据审查意见继续在原任务分支提交并推送，PR 会随之更新。负责人确认内容和检查结果后合并。接口修改需要同步更新 API 文档、消息示例和对应验证。

原因：PR 将代码差异、讨论、测试结果和合并记录集中起来，让 main 中的改动可审查、可追溯。

## 7. 合并后的同步与清理

确认 PR 已合并且工作区无未提交改动后执行：

```bash
git switch main
git pull --ff-only origin main
git fetch --prune
git branch -d feat/voice-wakeup
```

更新 main 获取团队成果；prune 清理已删除的远端分支引用；`branch -d` 尝试删除本地已合并分支。

若使用 Squash merge，Git 可能提示本地分支尚未合并。此时先核对 PR 确已合并、改动已进入 main，再决定删除，不要未经确认就强制删除分支。

## 8. 如何确认权限设置有效

由普通成员账号测试，而不是使用负责人管理员账号：

| 检查项 | 预期 |
| --- | --- |
| 推送任务分支 | 可以。 |
| 创建面向 main 的 PR | 可以。 |
| 直接更新 main | 被拒绝。 |
| 合并 PR 到 main | 普通成员被阻止，负责人可以通过 PR 合并。 |

不要通过删除 main 或强制推送来验证权限。需要实际测试写入拒绝时，优先在临时测试仓库中复现相同规则，避免配置错误时改动真实主分支。

只有“规则保存成功、确实启用、套餐允许执行、目标匹配正确、普通成员权限测试符合预期”，才能确认达到保护目标。

## 9. 团队约定

- 一项任务对应一个 Issue 和一个任务分支，尽量小批提交 PR。
- 游戏成员负责游戏逻辑，语音成员负责语音服务，通信协议由双方共同确认。
- 负责人维护需求、集成、合并和发布。
- 接口变更在同一个 PR 中更新文档和示例。
- main 应保持可集成状态；功能是否完整可用仍需本地游戏与音频验收。
