# 本 fork 的后续迭代

来源：[aliveranme/BBDown](https://github.com/aliveranme/BBDown)。原始项目：[nilaoda/BBDown](https://github.com/nilaoda/BBDown)。本页只记录 Surc fork 的后续调整，不复述上游功能、参数或安装手册。

## 当前状态

截至 2026-10-09，审核比较了：

- 历史修复提交：`21a4b3077e2c16b0ec9dbdac69171abacaccea91`，父提交 `03181b86610c1d5f98de1c8595f96bbc086c1525`，版本 v1.6.11。
- Fork `master` 与上游 `master`：`ca5b8384a773b1a3e72c193f9e8a803dde523714`，版本 v1.7.5。
- 9 组离线场景确认：v1.7.5 仍把 `PLAY_WHOLE` 判为限制；旧回退会遗漏 `PAY_LIMIT`，却对未知原因重试。该验证直接抽取对应提交的生产方法执行，没有访问真实播放接口，也没有运行新版完整 CI。

这些是带日期的审核记录，后续工作必须重新读取当前分支和上游提交。

## 下一次代码移植

### 第一阶段：整片可播修复和补充测试

从当时最新的 fork `master` 创建 `fix/` 分支，只移植 `PLAY_WHOLE` 判断和测试。保留新版的业务错误、风控、响应根定位和日志文本净化逻辑，避免用旧 Parser 覆盖新实现。

验收范围：

- `PLAY_WHOLE` 的空原因和 `PAY` 原因场景继续解析可用轨道。
- `PAY/PLAY_PREVIEW`、会员、区域、时间和未知限制仍给出原有错误；业务错误码和风控响应仍被检查。
- 保留历史新增的 3 个播放状态用例，并补一个通过 `IApiTransport` 注入完整响应的解析用例，确认真正进入轨道解析。
- 将历史 4 个封面/章节参数用例适配到新版混流入口，覆盖普通音视频/纯音频与有章节/无章节的组合。

生产代码不重复移植 mp4decrypt 参数和混流选项顺序：上游已分别在 [`f413525`](https://github.com/aliveranme/BBDown/commit/f413525ecfc9f69143dedf4ee583cfed6794b6a5) 和 [`91fac5d`](https://github.com/aliveranme/BBDown/commit/91fac5daf7cf1b08dfe3a52aeb1211b383b3ea56) 修复。SDK 使用当前基线要求，不带入历史的 `10.0.101` 降级。

### 第二阶段：重新设计 legacy MP4 回退

该功能目前只存在于历史分支；还没有移植到 v1.7.5。旧实现不直接复用，设计时处理：

- 使用响应中的结构化原因和详情判定，不匹配中文异常文案。明确列出可重试场景，对未知、区域、时间、风控和取消不作自动格式回退。
- 明确支持的 API 范围，例如 Web 番剧请求；不要让 TV、APP、INTL 无效地重复同一请求。
- 最多一次格式回退，后续 `durl` 最高画质请求保持 legacy 状态。
- 每轮响应都校验业务错误、播放状态、风控和有效轨道；不能把试看或空响应记为整片成功，也不承诺 legacy 与 DASH 画质相同。
- 请求失败、解析失败、校验失败、取消和成功路径均释放文档，保留新版取消传播及可用首轮响应降级逻辑。

用注入传输层的响应序列覆盖：成功回退、仍受限、未知限制、空 `durl`、最高画质重发失败、网络错误和用户取消。在线验证若执行，应仅记录结论，不提交凭据或签名 URL；没有实际触发回退时，不能据下载成功宣称回退已验证。

## 每次迭代留下什么

在变更说明中记录本次基线和提交、触发条件、前后行为、改动文件、验证命令和实际结果。把“已实现”“上游已覆盖”“计划中”“待验证”分开标注。README 只保留简短差异入口，详细调整写入 `LOCAL_CHANGES.md`，原版说明给链接。

代码检查从当前检出的 `AGENTS.md`、项目文件和 CI 配置取得命令，不复制旧 SDK 或旧测试过滤器。文档和 skill 的窄改动检查链接、编码、diff 和 skill 结构即可；未改代码时不重跑整个下载测试套件。

## 复用 skill

仓库中维护版本：[skills/bbdown-fork-iterate/SKILL.md](../skills/bbdown-fork-iterate/SKILL.md)。本机安装后可这样调用：

```text
使用 $bbdown-fork-iterate 审核当前 fork 与上游的差异，列出需要保留的调整。
使用 $bbdown-fork-iterate 把 PLAY_WHOLE 修复移植到当前 master，并补齐测试和差异说明。
使用 $bbdown-fork-iterate 审核 legacy 回退的实现和验证覆盖。
```

本机安装的是仓库 skill 的一份副本。更新仓库 skill 后，在用户要求更新本机版本时同步整个 skill 目录并再次校验；不要把仓库内的单独改动自动当作本机已安装更新。
