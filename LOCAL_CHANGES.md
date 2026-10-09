# 本地修改说明

## 来源与提交范围

- Fork 仓库：[Surc/BBDown](https://github.com/Surc/BBDown)。直接来源是 [aliveranme/BBDown](https://github.com/aliveranme/BBDown)，GitHub fork 网络的原始来源是 nilaoda/BBDown。
- 修改分支：`fix/local-playback-decrypt-mux`。
- 基线提交：[`03181b86610c1d5f98de1c8595f96bbc086c1525`](https://github.com/aliveranme/BBDown/commit/03181b86610c1d5f98de1c8595f96bbc086c1525)，项目版本为 `1.6.11`。
- 本次保存原有的 4 个文件修改，另补充 7 个回归用例、修改说明和 README 入口，并纠正一处对进程参数可见性的注释。
- 保留本地已使用的旧基线，没有合并当前上游 master，也没有把新上游文件覆盖回旧版本。该分支适合保存和复现本地版本；以后升级应按功能移植补丁并处理上游已有修复。

## 1. 正确识别整片可播状态

文件：`BBDown.Core/Parser.cs`，方法：`ThrowIfPlayLimited`。

原逻辑只要 `play_check` 的原因或详情字段非空就抛出异常，会把服务端返回的 `play_detail=PLAY_WHOLE` 也当作播放限制。在历史使用场景中，已登录并启用 DRM 解析时，服务端可能用该状态表示整片可播，原逻辑因此在轨道解析之前终止。

现在识别到 `PLAY_WHOLE` 时直接返回，继续原有解析流程。其他详情仍沿用原限制判断；没有删除业务错误码检查，也没有更改登录或许可证获取流程。

新增 3 个用例：空原因配合 `PLAY_WHOLE`、`PAY` 配合 `PLAY_WHOLE` 均不抛错；`PAY/PLAY_PREVIEW` 在限制检查方法中仍抛出包含原因与详情的错误。

## 2. 兼容官方 Bento4 mp4decrypt 参数

文件：`BBDown/Application/Decrypt.cs`，方法：`RunDecryptAsync`。

历史记录中使用的官方 Bento4 SDK 1.6.0-641 不支持原调用方式 `--key-file`，参数解析会报错，导致轨道下载后解密无法完成。本地修改改为其支持的 `--key <kid>:<key>`，并保留输入、输出文件路径的引号。

保留了外部进程启动失败检查、stderr 读取、非零退出码处理、失败输出清理、解密文件非空检查，以及取消/超时后的进程树终止逻辑。删除了不再使用的临时密钥文件创建、覆写和删除流程。

**兼容性取舍：** 密钥作为进程参数传递，有权限读取该进程命令行的本机用户可以看到密钥。注释已纠正为这一实际边界，不能认为参数“仅当前会话可读”。本次未加入真实密钥、登录凭据或媒体地址，也未重新执行在线 DRM 下载；历史成功记录不能代替本次端到端验证。

## 3. 修复封面与章节同时存在时的 ffmpeg 混流失败

文件：`BBDown/Infrastructure/BBDownMuxer.cs`，方法：`MuxAV`。

原逻辑先添加封面的 `-disposition:v:N attached_pic`，再添加章节 metadata 的 `-i` 输入。ffmpeg 会把位于后续输入前的输出选项解释到错误的位置，出现 `cannot be applied to input`，最终混流失败并留下已下载轨道。

现在将封面 disposition 统一追加到全部输入之后，再进入媒体映射和后续输出参数构造。普通音视频的封面仍使用 `v:1`，纯音频场景的封面仍使用 `v:0`；只添加一次 disposition，无章节时也正常添加。

新增 4 个参数回归用例，覆盖普通音视频/纯音频与有章节/无章节的组合，检查封面输出选项位于最后一个输入文件之后、轨道下标正确且不会重复。

这项本地补丁只移动封面 disposition；它不等同于新版上游对所有字幕和副音轨输出参数的统一重排。升级时应对照上游实际代码合并。

## 4. 在指定播放限制场景尝试 legacy MP4

文件：`BBDown.Core/Parser.cs`，方法：`GetPlayJsonAsync` 和 `ExtractTracksAsync`。

默认继续请求 DASH 格式 `fnval=4048`。当首次限制检查抛出消息包含“播放限制”的 `InvalidOperationException` 时，尝试一次 legacy 请求：

1. 请求参数切换为 `fnval=0`。
2. legacy 请求不再附加 `drm_tech_type=2`。
3. 重新解析响应，并再次执行播放限制和业务错误检查。
4. 返回 `durl` 时沿用原有 FLV/MP4 分段处理；重新请求最高画质时传递 legacy 状态，避免又切回 DASH。

该流程只是尝试另一种服务端格式，是否可下载仍取决于服务端响应。仍受限的 legacy 响应会继续报错，不会无条件放行。legacy 格式也不保证与 DASH 相同的清晰度、编码或音轨。

**现有实现范围：** catch 通过中文错误消息匹配触发，而非结构化限制代码；`PAY/PLAY_PREVIEW` 等落入通用“播放限制”消息的场景可触发，具有单独错误文案的 `PAY_LIMIT`、`VIP_LIMIT`、`AREA_LIMIT`、`TIME_LOCK` 不会因此自动回退。`fnval` 修改位于 Web 请求构造分支，不能把它理解为所有 TV/APP 接口均有相同效果。

历史对话记录曾观察到 DASH 受限、legacy 返回 `PLAY_WHOLE`，但补丁后的实际补下载恢复使用 DASH，并未触发回退。因此本次不宣称已经完成 legacy 回退的在线端到端验证；也没有为该流程新增模拟网络测试。

## 5. 本机 .NET SDK 调整

文件：`global.json`。

SDK 从 `10.0.300` 改为本机已安装的 `10.0.101`；保留 `rollForward=latestPatch` 和 `allowPrerelease=false`。这项改动复现原本地构建条件，项目仍目标 `net10.0`，版本仍为 `1.6.11`，没有更改 NuGet 依赖。

由于仍使用 `latestPatch`，这不是任意 .NET 10 SDK 都可用的配置；其他机器和 CI 应安装匹配的 SDK，或在后续独立调整版本策略。

## 本次验证（2026-10-09）

环境：Windows，.NET SDK `10.0.101`，本机可用 ffmpeg。测试临时目录设置在可写工作区，使用本机已有 NuGet 缓存完成还原；本次还原关闭在线 NuGet 漏洞查询，这不是漏洞审计结论。

| 检查 | 结果 |
| --- | --- |
| Release 编译 | 成功，0 警告、0 错误 |
| ParserTests、ParserPlayLimitTests、MuxerArgsTests | 31/31 通过，包含新增的 7 个用例 |
| 现有真实 ffmpeg 冒烟用例 | 音视频混流、跳过空字幕后混流均实际执行并通过 |
| 排除 `Category=Integration` 的完整测试 | 401 项，390 通过、11 失败 |
| `git diff --check` | 通过 |

完整测试不能报告为全部通过。剩余失败为 9 个 HTTP 重定向/下载用例在 `HttpListener.Start` 时“拒绝访问”，以及 2 个外部进程取消/超时用例：这些用例用本机 ping 模拟常驻进程，但当前沙箱的 ping 立即报 `Unable to contact IP driver`，因而没有等待到取消或超时。上述测试文件和生产实现未在本次修改。最初默认沙箱临时目录下另外 7 项文件操作失败，在改用工作区临时目录后已通过。

可复现的主要命令：

```powershell
dotnet restore --ignore-failed-sources -p:NuGetAudit=false -m:1 -nr:false
dotnet build -c Release --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
dotnet test BBDown.Tests/BBDown.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter "FullyQualifiedName~ParserTests|FullyQualifiedName~ParserPlayLimitTests|FullyQualifiedName~MuxerArgsTests"
dotnet test BBDown.Tests/BBDown.Tests.csproj -c Release --no-build --no-restore -m:1 -nr:false --filter "Category!=Integration"
```

## 交付边界

原本地源码目录保留不动。本次只上传明确的源码、SDK 配置、测试和文档差异，不上传 OpenCode 对话 JSON、Cookie、签名下载链接、登录数据、调试日志、下载媒体或构建缓存。基线中原有的文件与历史按 fork 关系继承，未将其描述为本次新增。

本次没有发布 release、修改版本号、更新 fork 默认分支、合并新上游或向上游创建 PR。修复代码需从本说明列出的分支查看和检出。
