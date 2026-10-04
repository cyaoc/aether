# Issue tracker: GitHub

本仓库的任务和规格存放在 cyaoc/aether 的 GitHub Issues。
使用 gh CLI 操作，在仓库目录内执行以自动识别远端。

## 操作约定

- 创建：gh issue create --title "..." --body-file <文件路径>
- 读取：gh issue view <编号> --comments
- 列表：gh issue list --state open --json number,title,body,labels,comments
- 评论：gh issue comment <编号> --body-file <文件路径>
- 添加标签：gh issue edit <编号> --add-label "<标签>"
- 移除标签：gh issue edit <编号> --remove-label "<标签>"
- 关闭：gh issue close <编号> --comment "..."

多行正文先写入临时文件，再通过 --body-file 提交。
标签含义见 triage-labels.md。

技能要求“publish to the issue tracker”时，创建 GitHub Issue。
技能要求“fetch the relevant ticket”时，读取对应 Issue 及评论。

## Pull requests as a triage surface

**PRs as a request surface: no.**

## Wayfinding operations

- Map：使用一个带 wayfinder:map 标签的 Issue，
  保存 Notes、Decisions-so-far 和 Fog。
- 子任务：链接为 Map 的 GitHub sub-issue。
  不支持时，在 Map 正文维护任务列表，并在子任务顶部写 Part of #<map>。
- 类型：使用 wayfinder:research、wayfinder:prototype、
  wayfinder:grilling 或 wayfinder:task 标签。
- 依赖：优先使用 GitHub 原生 Issue dependencies。
  不支持时，在子任务顶部写 Blocked by: #<编号>, #<编号>。
- 可执行任务：按 Map 中的顺序，选择尚未关闭、没有未完成依赖、
  且没有 assignee 的第一个子任务。
- 认领：开始工作前执行 gh issue edit <编号> --add-assignee @me。
- 完成：评论记录结果，关闭子任务，并在 Map 的
  Decisions-so-far 中追加结论摘要和链接。
