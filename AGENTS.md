# Surc/BBDown 迭代约定

本文件只补充本 fork 的维护约定。通用开发约束以当前基线的上游文件为准：[上游 AGENTS.md](https://github.com/aliveranme/BBDown/blob/master/AGENTS.md)、[贡献指南](https://github.com/aliveranme/BBDown/blob/master/CONTRIBUTING.md)。切换基线后，以实际检出的版本为准，不把远端 master 的规则或版本号当作旧分支现状。

- 维护本 fork 时先读 [迭代 skill](skills/bbdown-fork-iterate/SKILL.md)。当前改动见 [LOCAL_CHANGES.md](LOCAL_CHANGES.md)，下一步的范围与验收条件见 [迭代说明](docs/ITERATION.md)。
- `origin` 应指向 `Surc/BBDown`，`upstream` 应指向 `aliveranme/BBDown`；操作前读取实际 remote，避免改到原始工作目录或上游仓库。
- `fix/local-playback-decrypt-mux` 保存 v1.6.11 的历史本地补丁；不要把这个基线整段覆盖到新版 `master`。移植时按功能选择差异，再核对上游是否已经覆盖。
- 评审、实现、推送和合并按用户本次请求执行。修改 `master` 应通过 PR；创建 skill 不代表获得额外的推送、合并、发布或凭据权限。
- README 和自有说明只写本 fork 的调整、证据与状态，通用功能和用法链接上游。保留继承的许可证和归属声明。
- 新增 Markdown/YAML 使用 UTF-8、LF、文件末尾换行。修改 skill 后运行 skill-creator 的 `quick_validate.py`；修改代码时按当前基线的测试和 CI 要求验证。
- 当前上游和历史补丁的测试结果分开记录。注明日期、提交、命令、通过/失败/未运行的范围，不能把旧测试计数当作新版已验证结果。
- 不把 Cookie、密钥、签名媒体地址、登录文件或原始对话放进 skill、文档或提交。
