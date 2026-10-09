using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Threading;
using BBDown;
using BBDown.Core;
using BBDown.Core.Util;

namespace BBDown.Commands;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
public class WatchLaterSettings : DownloadOptionSettings
{
    [CommandOption("--limit")]
    [Description("最多下载前 N 个稍后再看视频(默认 0=全部)")]
    public int Limit { get; set; }
}

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
public class WatchLaterCommand : AsyncCommand<WatchLaterSettings>
{
    protected override async Task<int> ExecuteAsync(CommandContext context, WatchLaterSettings settings, CancellationToken cancellationToken)
    {
        try
        {
            // -w 必须先绝对化且只解析一次（RF-90）：ChangeWorkingDir（CLI 非 serve）会把进程
            // CWD 切到上一个视频的下载目录，相对 -w 到第二个视频会基于该目录再拼一层，
            // 产出 <root>/<av1>/<av2> 嵌套（与 SubCommand 同源缺陷）。
            // 非法 -w 是整批无效的输入错误：明确报错 + 退出码 1，不让 Path.GetFullPath 的
            // ArgumentException 逃到命令级异常处理器（那会误报"请尝试升级到最新版本后重试!"）。
            if (!Program.TryResolveWorkDir(settings.WorkDir, out string resolvedWorkDir, out string workDirError))
            {
                Logger.LogError($"工作目录无效: {workDirError}");
                return 1;
            }
            settings.WorkDir = resolvedWorkDir;

            // 稍后再看接口需要登录：先加载本地登录凭据（或用户传入的 cookie）。
            // 用统一会话初始化入口：不仅加载凭据，还做登录检查并提取 wbi——
            // 稍后再看列表与后续下载都用 WEB API，空 wbi 的 w_rid 会被 B 站拒绝。
            // 返回的完整会话在父流程自身异步流内应用（子方法内 AsyncLocal 写入不回流）。
            var bootstrap = new MyOption { Cookie = settings.Cookie, AccessToken = settings.AccessToken, UseTvApi = settings.UseTvApi, UseAppApi = settings.UseAppApi, UseIntlApi = settings.UseIntlApi };
            var session = await Program.InitializeRequestSessionAsync(bootstrap, cancellationToken);
            if (session is not null) Core.Config.Apply(session);

            Logger.Log("正在获取稍后再看列表...");
            var list = await FetchWatchLaterAsync(cancellationToken);
            if (list.Count == 0)
            {
                Logger.Log("稍后再看列表为空");
                return 0;
            }

            var targets = settings.Limit > 0 ? list.Take(settings.Limit).ToList() : list;
            Logger.Log($"共 {list.Count} 个稍后再看，开始下载 {targets.Count} 个...");
            int succeeded = 0;
            int failed = 0;
            foreach (var (aid, title) in targets)
            {
                // RF-70：title 来自服务器原文（item.GetValueAsStringSafe），可含 CRLF 伪造日志行；
                // 与 RF-54 同构过 SanitizeLogString 后再写日志。aid 同为服务器原文，同处净化。
                Logger.Log($"--- 下载 av{BBDownApiServer.SanitizeLogString(aid)} {BBDownApiServer.SanitizeLogString(title)} ---");
                try
                {
                    var opt = settings.ToMyOption($"av{aid}", settings.WorkDir);
                    await Program.DoWorkAsync(opt, cancellationToken);
                    succeeded++;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                // 与 sub check / 下载编排共用同一份"单条目可跳过"策略（此前是三份手工同步的
                // 类型集合：RF-44/RF-72 的扩充就漏过本处）。用户取消已由上方带 token 守卫的
                // catch 重抛，谓词内的 TaskCanceledException 只会命中 HttpClient 超时。
                catch (Exception ex) when (ExceptionPolicies.IsSkippableItemFailure(ex))
                {
                    // 单个视频失败不应中止整批稍后再看，但必须计入失败数，
                    // 让调用方拿到非零退出码（此前静默继续并返回 0，
                    // 脚本/CI 无法区分"全部成功"与"部分失败"）
                    failed++;
                    Logger.LogWarn($"av{BBDownApiServer.SanitizeLogString(aid)} 下载失败（继续下一个）: {ex.Message}");
                }
            }
            Logger.Log($"稍后再看下载完成：成功 {succeeded} 个，失败 {failed} 个");
            return failed == 0 ? 0 : 1;
        }
        catch (OperationCanceledException ex)
        {
            // 区分主动取消（Ctrl+C，token 已取消）与 HttpClient 超时（token 未取消）：
            // 超时是真实失败，必须返回非零退出码，不能以"已取消"+0 隐藏失败（脚本/CI 误判成功）
            if (cancellationToken.IsCancellationRequested)
            {
                Logger.LogWarn("已取消");
                return 0;
            }
            Logger.LogError($"稍后再看下载超时或被中断: {ex.Message}");
            return 1;
        }
        catch (Exception ex)
        {
            Logger.LogError($"稍后再看下载失败: {ex.Message}");
            return 1;
        }
    }

    private static async Task<List<(string Aid, string Title)>> FetchWatchLaterAsync(CancellationToken token)
    {
        const string api = "https://api.bilibili.com/x/v2/history/toview";
        string json = await HTTPUtil.GetWebSourceAsync(api, token: token);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        int code = root.GetInt32Safe("code");
        if (code != 0)
            throw new InvalidOperationException($"获取稍后再看失败(code={code}): {root.GetValueAsStringSafe("message")}。该接口需要登录，请先运行 BBDown login 或传入 --cookie。");

        var list = new List<(string, string)>();
        var dataElem = root.TryGetPropertySafe("data");
        if (dataElem is not null)
        {
            foreach (var item in dataElem.Value.EnumerateArraySafe("list"))
            {
                var aid = item.GetValueAsStringSafe("aid");
                if (aid == "") continue;
                list.Add((aid, item.GetValueAsStringSafe("title")));
            }
        }
        return list;
    }
}
