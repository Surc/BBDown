# 本 fork 的修改与审核记录

本页只说明 Surc fork 的调整。原版功能、使用方法和安装说明见 [aliveranme/BBDown](https://github.com/aliveranme/BBDown#readme)；最初项目为 [nilaoda/BBDown](https://github.com/nilaoda/BBDown)。

## 范围与基线

- Fork：[Surc/BBDown](https://github.com/Surc/BBDown)。历史修复分支：`fix/local-playback-decrypt-mux`。
- 代码基线：[`03181b8`](https://github.com/aliveranme/BBDown/commit/03181b86610c1d5f98de1c8595f96bbc086c1525)，v1.6.11。
- 历史修复：[`21a4b30`](https://github.com/Surc/BBDown/commit/21a4b3077e2c16b0ec9dbdac69171abacaccea91)，保存 4 个原有文件修改，补充 7 个回归用例及说明。
- 审核对照：2026-10-09，fork 与直接上游的 `master` 均为 [`ca5b838`](https://github.com/aliveranme/BBDown/commit/ca5b8384a773b1a3e72c193f9e8a803dde523714)，v1.7.5。

历史分支用于保存本地版本。以下“已实现”指历史修复提交，“上游已覆盖”指上述审核基线，“待移植”是后续工作；本次文档补充没有将代码移植到新版或合并到 `master`。

## 1. 整片播放状态修复：已实现，值得移植

文件：`BBDown.Core/Parser.cs`，方法：`ThrowIfPlayLimited`。

原判断把非空的播放限制字段直接视为限制，导致 `play_detail=PLAY_WHOLE` 也被拒绝。历史修复识别该状态后继续解析，其他限制仍沿用原检查；业务错误码、登录和许可证流程没有因此取消。

新增 3 个方法级用例：空原因与 `PLAY_WHOLE`、`PAY` 与 `PLAY_WHOLE` 均继续；`PAY/PLAY_PREVIEW` 仍抛出限制错误。

v1.7.5 对照代码仍有这项误判，最小行为修复值得保留。移植时必须保留新版的业务错误、风控、响应定位和日志文本净化，并补充从完整响应进入轨道解析的用例。具体验收条件见 [迭代说明](docs/ITERATION.md#第一阶段整片可播修复和补充测试)。

## 2. mp4decrypt 参数：历史已实现，新版上游已覆盖

文件：`BBDown/Application/Decrypt.cs`，方法：`RunDecryptAsync`。

历史环境中的官方 Bento4 SDK 1.6.0-641 不接受旧调用的 `--key-file`。修复改为 `--key <kid>:<key>`，删除不再需要的临时密钥文件流程，保留进程失败检查、stderr 读取、输出非空检查、失败清理和取消/超时终止逻辑。

实际兼容性边界是：有权限读取该进程命令行的本机用户可以看到参数中的密钥。原注释中“仅当前会话可读”的说法已纠正。

新版上游在 [`f413525`](https://github.com/aliveranme/BBDown/commit/f413525ecfc9f69143dedf4ee583cfed6794b6a5) 已采用官方参数，且有独立进程参数构造、唯一输出路径与失败时保留输入等处理。升级时使用新版实现，不重复移植旧生产代码。此轮没有重新执行在线 DRM 下载。

## 3. 封面与章节混流：历史已实现，新版上游已覆盖

文件：`BBDown/Infrastructure/BBDownMuxer.cs`，方法：`MuxAV`。

旧逻辑在章节 metadata 的 `-i` 输入前添加封面的 `-disposition:v:N attached_pic`。ffmpeg 会把输出选项解释到输入侧并报 `cannot be applied to input`，导致混流失败。

历史修复把封面 disposition 放到所有输入之后。普通音视频使用封面轨 `v:1`，纯音频使用 `v:0`，只添加一次。新增 4 个参数用例，覆盖普通音视频/纯音频与有章节/无章节的组合，检查顺序、轨道下标和重复选项。

新版上游在 [`91fac5d`](https://github.com/aliveranme/BBDown/commit/91fac5daf7cf1b08dfe3a52aeb1211b383b3ea56) 已统一重排输出选项，范围还包括字幕和副音轨。保留新版生产实现，历史 4 个用例可适配为额外回归覆盖。

## 4. legacy MP4 回退：历史已实现，移植前需重写

文件：`BBDown.Core/Parser.cs`，方法：`GetPlayJsonAsync`、`ExtractTracksAsync`。

历史实现默认请求 DASH（`fnval=4048`）；首次限制检查的 `InvalidOperationException` 消息包含“播放限制”时，尝试一次 legacy 请求（`fnval=0`，不附加 `drm_tech_type=2`）。新响应继续检查业务错误与播放限制；有 `durl` 时走原有分段处理，最高画质重请求保留 legacy 状态。

审核发现三项需要调整：

1. 中文异常文案不能准确表达可重试范围：`PAY/PLAY_PREVIEW` 可触发，但单独文案的 `PAY_LIMIT` 被遗漏，未知原因反而可能触发。
2. 参数变化只适用于相应 Web 请求分支，不能让 TV、APP 等接口无效重发相同请求。
3. 首轮限制检查及回退发生在现有文档释放范围之外，回退请求或解析失败时存在文档未释放的路径；重写应明确每轮响应的所有权、取消传播和失败处理。

新版上游尚未包含该回退。后续应按结构化状态、明确 API 范围和最多一次重试重新设计，并覆盖 `durl` 重请求与所有退出路径。legacy 不保证与 DASH 相同的清晰度、编码和音轨；仍受限、试看或空轨道不能记为整片成功。

历史对话曾观察到 legacy 返回 `PLAY_WHOLE`，但补丁后的实际补下载恢复使用 DASH，并未触发回退。本轮未做在线回退验证，也未为旧实现新增模拟网络测试。计划和验收见 [迭代说明](docs/ITERATION.md#第二阶段重新设计-legacy-mp4-回退)。

## 5. 本机 SDK：仅用于历史构建

文件：`global.json`。历史修复把 SDK `10.0.300` 改为本机安装的 `10.0.101`，保留 `latestPatch` 与 `allowPrerelease=false`，项目仍为 `net10.0`、v1.6.11。

这不是新版升级所需的补丁。后续按当前基线的 SDK、依赖锁文件和 CI 要求构建，不把该降级带入 v1.7.5。

2026-10-10，为验证历史本地补丁，独立分支 `ci/cloud-validation-local-fixes` 将 SDK 恢复为 `10.0.300`，与该历史版本已有的 PR workflow 对齐。应用仍为 v1.6.11；构建要求的变化及云端检查记录见 [云端验证](docs/CLOUD_TESTING.md)。原修复分支和原版备份 tag 保留各自的版本。

## 验证记录

以下记录均发生在 2026-10-09，不能作为后续提交已通过检查的证明。

### 历史修复提交 21a4b30

环境为 Windows、.NET SDK `10.0.101`，ffmpeg 可用，测试临时目录位于可写工作区。依赖使用已有缓存还原，此次关闭在线 NuGet 漏洞查询，未执行漏洞审计。

| 检查 | 实际结果 |
| --- | --- |
| Release 编译 | 成功，0 警告、0 错误 |
| ParserTests、ParserPlayLimitTests、MuxerArgsTests | 31/31 通过，包括新增 7 个用例 |
| 现有真实 ffmpeg 冒烟用例 | 音视频混流、跳过空字幕后混流实际执行并通过 |
| 排除 `Category=Integration` 的测试 | 401 项，390 通过、11 失败 |

11 项失败包括：9 项在 `HttpListener.Start` 遇到“拒绝访问”；2 项用 ping 模拟常驻进程，但沙箱中 ping 立即报 `Unable to contact IP driver`，未达到预期取消/超时场景。最初另有 7 项在默认沙箱临时目录中失败，改用工作区临时目录后通过。相关生产实现和测试文件未为这些环境问题修改。

历史复现命令如下；新版必须使用其自身的检查要求：

```powershell
dotnet restore --ignore-failed-sources -p:NuGetAudit=false -m:1 -nr:false
dotnet build -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test BBDown.Tests/BBDown.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter "FullyQualifiedName~ParserTests|FullyQualifiedName~ParserPlayLimitTests|FullyQualifiedName~MuxerArgsTests"
dotnet test BBDown.Tests/BBDown.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter "Category!=Integration"
```

### v1.7.5 与历史实现的离线审核

抽取对应提交的生产方法执行 9 组离线场景，确认整片误判与 legacy 判定差异。这是方法级复现，没有访问真实播放接口，没有运行 v1.7.5 完整构建、测试或 CI。

## 文档提交 95879d1 与 skill 补充

该提交只调整说明与维护入口：README 改为 fork 差异摘要并链接原版；更新本页的保留结论与验证边界；新增 [迭代说明](docs/ITERATION.md)、[维护约定](AGENTS.md)、[bbdown-fork-iterate skill](skills/bbdown-fork-iterate/SKILL.md) 及其调用元数据。该提交没有修改代码、SDK 和原始工作目录。

后续迭代按功能移植，更新本页实际完成的调整与验证记录。保持“已实现”“上游已覆盖”“计划中”“待验证”可区分，不提交原始对话、Cookie、密钥、签名地址、登录文件、下载媒体和构建缓存。
