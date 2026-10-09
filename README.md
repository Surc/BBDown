# BBDown · Surc fork 调整说明

本仓库保存本地修复及后续迭代记录。本页只说明 fork 的调整；通用功能、安装方式、命令参数和教程请查看原版：

- 直接来源：[aliveranme/BBDown](https://github.com/aliveranme/BBDown)，[原版说明](https://github.com/aliveranme/BBDown#readme)、[Wiki](https://github.com/aliveranme/BBDown/wiki)。
- 最初项目：[nilaoda/BBDown](https://github.com/nilaoda/BBDown)。

继承的许可证见 [LICENSE](LICENSE)，原项目归属保留。

## 当前分支与调整

`fix/local-playback-decrypt-mux` 保存基于 **v1.6.11** 的本地修改，代码提交为 [`21a4b30`](https://github.com/Surc/BBDown/commit/21a4b3077e2c16b0ec9dbdac69171abacaccea91)。截至 2026-10-09，它尚未合并到 fork 的 `master`；`master` 是 v1.7.5 的上游基线。后续的文档提交不会改变上述代码版本。

| 调整 | 历史分支中的行为 | 升级到新版时的审核结论 |
| --- | --- | --- |
| 整片播放状态 | `PLAY_WHOLE` 不再被误判为播放限制 | 保留最小修复，结合新版解析流程补测试 |
| mp4decrypt 参数 | 改用 `--key <kid>:<key>` | 新版上游已覆盖，使用上游实现 |
| 封面与章节混流 | 封面输出选项移到所有输入之后 | 新版上游已统一处理输出选项；保留有价值的测试 |
| legacy MP4 回退 | 特定限制异常后尝试一次 `fnval=0` | 旧实现判定范围不准确，需要重写后再移植 |
| 本机 SDK | 历史构建使用 .NET SDK `10.0.101` | 新版按其自身 SDK 和 CI 要求构建 |

上述结论来自指定提交的审核，不表示新版代码已经完成移植。详细原因、文件范围和验证边界见 [修改说明](LOCAL_CHANGES.md)。

## 后续迭代入口

- [迭代说明](docs/ITERATION.md)：审核基线、应保留的内容、移植顺序和验收条件。
- [云端验证](docs/CLOUD_TESTING.md)：独立验证分支的 SDK 调整、检查范围和实际运行记录。
- [维护约定](AGENTS.md)：本 fork 的文档和交付约定。
- [迭代 skill](skills/bbdown-fork-iterate/SKILL.md)：可复用的审核、移植、验证和差异文档流程，配套元数据位于同目录的 `agents/openai.yaml`。

将 `skills/bbdown-fork-iterate` 整个目录放入本机 Codex 的 skills 目录后，可以这样调用：

```text
使用 $bbdown-fork-iterate 审核当前 fork 与上游的差异，列出需要保留的调整。
使用 $bbdown-fork-iterate 把 PLAY_WHOLE 修复移植到当前 master，并补齐测试和差异说明。
```

仓库内 skill 和本机安装副本分别保存；修改仓库版本后，需要同步安装副本才能使用更新的流程。

## 验证记录

2026-10-09 对历史修复提交的 Release 编译通过；相关测试 **31/31** 通过。排除 `Category=Integration` 的测试为 **390/401** 通过，另 11 项在当前沙箱中失败。方法级离线审核覆盖 9 组场景，没有执行新版完整 CI 或在线 legacy 回退验证。

完整结果和失败原因见 [修改说明中的验证记录](LOCAL_CHANGES.md#验证记录)。每次代码迭代应另记实际基线与结果，不能沿用这组历史计数。
