# 历史本地补丁的云端验证

原版：[aliveranme/BBDown](https://github.com/aliveranme/BBDown)。本页只说明本 fork 为云端验证做的调整及结果，不复制原版使用手册。

## 为什么调整 SDK

测试对象是基于 v1.6.11 的本地修复和文档提交 `95879d10c4cdda90353792a8088aea6a54c9ad56`，不是 v1.7.5 移植版。

历史本机 SDK 为 `10.0.101`，而该版本已有的 `.github/workflows/pr.yml` 安装 `10.0.300`。`global.json` 使用 `latestPatch`，不会跨 SDK 功能版本段选择 SDK，因此仅安装 CI 的 SDK 不能满足本机降级后的配置。

独立验证分支 `ci/cloud-validation-local-fixes` 将 `global.json` 恢复为 `10.0.300`，保留 `latestPatch` 和 `allowPrerelease=false`，使现有 CI 能构建。此分支通过以 `fix/local-playback-decrypt-mux` 为目标的草稿 PR 运行检查；原修复分支、master 和两个原版备份 tag 不因测试分支的 SDK 调整而改变。

## 检查范围

使用已有 PR workflow 的 Ubuntu 云端 runner：

| 检查 | 内容 |
| --- | --- |
| Build & Test | 还原、Release 编译；排除 Integration、NetworkIntegration、LocalIntegration 的单元测试 |
| Integration Tests (local ffmpeg) | 安装 ffmpeg，执行真实混流冒烟 |
| Integration Tests (external network) | 匿名访问 B 站的既有网络集成测试；该 job 的失败需单独报告 |
| Native AOT smoke (linux-x64) | 编译 Native AOT 二进制并运行版本检查 |
| Format Check | 既有格式门禁 |
| NuGet Vulnerability Scan | 既有直接及传递依赖漏洞扫描 |

需要重新观察先前在 Windows 沙箱失败的 HTTP 监听与进程取消/超时用例。Ubuntu 的进程用例使用 Unix 常驻进程；其结果不能代替 Windows 分支的 ping 实现验证。

这些检查不使用用户 Cookie、真实 DRM 密钥或签名媒体地址；既有网络集成测试也不等于在线 DRM 或 legacy 回退验证。

## 运行记录

2026-10-10：验证分支已准备。GitHub API 返回 Actions workflow 列表为空，启用现有 `pr.yml` 返回 404；浏览器控制连接失败。尚未获得云端执行结果，不能把本地历史测试计数写成云端通过。

云端实际运行后，应补充 PR、run URL、受测提交、各 job 结果和通过/失败计数。失败按日志区分环境、上游基线与补丁行为，不因 `continue-on-error` 使整体 workflow 成功就忽略网络 job 的失败。
