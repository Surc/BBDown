using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using static BBDown.Core.Entity.Entity;
using BBDown.Core;
using BBDown.Core.Entity;

using BBDown.Core.Util;
using System.Text.Json;
namespace BBDown;

internal partial class Program
{
    /// <summary>
    /// 选项预处理 + 配置应用，产出本次任务的 <see cref="DownloadContext"/>（I5：原 9 元组）。
    /// </summary>
    public static DownloadContext SetUpWork(MyOption myOption)
    {
        //处理废弃选项
        HandleDeprecatedOptions(myOption);

        //处理冲突选项
        HandleConflictingOptions(myOption);

        //校验数值选项，避免非法值在下载或混流阶段以离奇的方式失败
        ValidateNumericOptions(myOption);

        //寻找并设置所需的二进制文件路径
        FindBinaries(myOption);

        //切换工作目录（返回解析后的绝对目录，并入下方 AppSettings 的 WorkDir）。
        // 不能在 ChangeWorkingDir 内部自行写配置：下方 Config.Apply(new AppSettings(...))
        // 会整体替换配置快照，WorkDir 会被重置为空。
        bool isServeMode = Config.Current.IsServeMode;
        string workDir = ChangeWorkingDir(myOption);

        //解析优先级
        var encodingPriority = ParseEncodingPriority(myOption, out var firstEncoding);
        var dfnPriority = ParseDfnPriority(myOption);

        bool downloadDanmaku = myOption.DownloadDanmaku || myOption.DanmakuOnly;
        BBDownDanmakuFormat[] downloadDanmakuFormats = ParseDownloadDanmakuFormats(myOption);

        string input = myOption.Url;
        string lang = myOption.Language;
        string aidOri = ""; // 用户输入的原始资源标识（URL/BV/av 等解析前的形态），
                            // 供 Parser.ExtractTracksAsync 的 aidOri 参数做回退/展示——
                            // 解析出的真实 aid 由 UrlResolver 结果与各分P 结构承载。
        int delay = myOption.DelayPerPage;
        Config.Apply(new AppSettings(
            Cookie: myOption.Cookie,
            // System.Text.Json 对 JSON 中显式 null 会覆盖属性初始化器，serve 请求体
            // {"accessToken": null} 可把 AccessToken 置为 null，这里必须防空
            Token: (myOption.AccessToken ?? "").Replace("access_token=", ""),
            DebugLog: myOption.Debug,
            Host: myOption.Host,
            EpHost: myOption.EpHost,
            TvHost: myOption.TvHost,
            Area: myOption.Area,
            SkipSslCheck: myOption.Insecure,
            MuxerTimeoutMinutes: myOption.MuxerTimeout,
            MaxRetryCount: myOption.RetryCount,
            RetryDelayMs: myOption.RetryDelay,
            ThreadSegmentSizeMb: myOption.ThreadSegmentSize,
            // UA 按异步流隔离写入 Config.Current，HTTPUtil.GetUserAgent 读它：
            // 不再改进程级静态 HTTPUtil.UserAgent，serve 并发任务互不污染
            UserAgent: myOption.UserAgent,
            // 任务流工作目录：serve 下经 AsyncLocal 隔离，PathUtil.ResolveWorkPath 据此解析相对路径
            WorkDir: workDir,
            IsServeMode: isServeMode
        ));

        Logger.LogDebug("AppDirectory: {0}", APP_DIR);
        if (Config.Current.DebugLog)
        {
            var savedCookie = myOption.Cookie;
            var savedToken = myOption.AccessToken;
            myOption.Cookie = string.IsNullOrEmpty(savedCookie) ? "" : "***";
            myOption.AccessToken = string.IsNullOrEmpty(savedToken) ? "" : "***";
            Logger.LogDebug("运行参数：{0}", JsonSerializer.Serialize(myOption, MyOptionJsonContext.Default.MyOption));
            myOption.Cookie = savedCookie;
            myOption.AccessToken = savedToken ?? "";
        }
        return new DownloadContext(encodingPriority, dfnPriority, firstEncoding, downloadDanmaku,
            downloadDanmakuFormats, input, lang, aidOri, delay);
    }

    public static async Task<(string fetchedAid, VInfo vInfo, string apiType, AppSettings? session)> GetVideoInfoAsync(MyOption myOption, string aidOri, string input, CancellationToken cancellationToken = default)
    {
        // 统一初始化请求会话：加载凭据 + 登录检查 + 提取 wbi。返回完整的会话配置
        // （含本地 BBDown.data 加载出的 Cookie/Token，以及提取的 wbi），由调用方在
        // 自身异步流内 Config.Apply 一次性应用——子方法内的 AsyncLocal 写入不会回流
        // 父流程（见 ConfigPropagationTests），只返回 newWbi 会让本地凭据在返回后丢失。
        AppSettings? session = await InitializeRequestSessionAsync(myOption, cancellationToken);
        if (session is not null) Config.Apply(session);

        Logger.Log("获取aid...");
        aidOri = await UrlResolver.ResolveAsync(input, cancellationToken);
        Logger.Log($"获取aid结束: {aidOri}");

        if (string.IsNullOrEmpty(aidOri))
        {
            throw new ArgumentException("输入有误：无法识别的视频 URL 或 ID");
        }

        Logger.Log("获取视频信息...");
        IFetcher fetcher = FetcherFactory.CreateFetcher(aidOri, myOption.UseIntlApi);
        VInfo? vInfo = null;

        // 只输入 EP/SS 时优先按番剧查找，如果找不到则尝试按课程查找
        try
        {
            vInfo = await fetcher.FetchAsync(aidOri, cancellationToken);
        }
        // 回退只对 ep: 前缀成立：其余输入（mid:、favlist:、listBizId: 等）与课程无关，
        // 此前它们同样会走进这里，打印"未找到此 EP/SS 对应番剧信息"这类无关提示，
        // 再用未经改动的 aidOri 重试一次——既误导用户，也把失败的请求翻倍。
        catch (Exception e) when (e is KeyNotFoundException or InvalidOperationException
                                  && aidOri.StartsWith("ep:"))
        {
            // B站返回非番剧JSON结构（可能是课程），尝试按课程查找
            Logger.LogWarn("未找到此 EP/SS 对应番剧信息, 正在尝试按课程查找。");

            aidOri = "cheese:" + aidOri[3..];
            Logger.Log("新的 aid: " + aidOri);

            if (string.IsNullOrEmpty(aidOri))
            {
                throw new ArgumentException("输入有误：无法获取视频信息");
            }

            Logger.Log("获取视频信息...");
            fetcher = FetcherFactory.CreateFetcher(aidOri, myOption.UseIntlApi);
            vInfo = await fetcher.FetchAsync(aidOri, cancellationToken);
        }

        string title = vInfo.Title;
        long pubTime = vInfo.PubTime;
        Logger.LogColor("视频标题: " + title);
        if (pubTime != 0)
        {
            Logger.Log("发布时间: " + FormatTimeStamp(pubTime, "yyyy-MM-dd HH:mm:ss zzz"));
        }
        var bvid = vInfo.PagesInfo.FirstOrDefault()?.bvid;
        if (!string.IsNullOrEmpty(bvid) && !myOption.UseIntlApi)
        {
            Logger.Log($"视频URL: https://www.bilibili.com/video/{bvid}/");
        }
        var mid = vInfo.PagesInfo.FirstOrDefault(p => !string.IsNullOrEmpty(p.ownerMid))?.ownerMid;
        if (!string.IsNullOrEmpty(mid))
        {
            Logger.Log($"UP主页: https://space.bilibili.com/{mid}");
        }

        if (vInfo.IsSteinGate && myOption.UseTvApi)
        {
            Logger.Log("视频为互动视频，暂时不支持tv下载，修改为默认下载");
            myOption.UseTvApi = false;
            // 降级后不再消费 TV token，回到网页接口：本地没有网页 Cookie 时试看限制确实
            // 适用，补打此前被 token 模式分流抑制的未登录横幅（互动视频 + 仅 token 用户）。
            if (string.IsNullOrEmpty(Config.Current.Cookie)) LogNotLoggedInBanner();
        }
        // 与 Parser 的实际分派同源（此前这里是 TV > APP > INTL，而分派是 INTL > APP > TV，
        // 同时给出多个 --use-*-api 时展示值会与实际走的接口不一致）。
        string apiType = Parser.ApiModeLabel(Parser.ResolveApiMode(myOption.UseTvApi, myOption.UseIntlApi, myOption.UseAppApi));

        //打印分P信息
        List<Page> pagesInfo = vInfo.PagesInfo;
        bool more = false;
        foreach (Page p in pagesInfo)
        {
            if (!myOption.ShowAll)
            {
                if (more && p.index != pagesInfo.Count) continue;
                if (!more && p.index > 5)
                {
                    Logger.Log("......");
                    more = true;
                    continue;
                }
            }

            Logger.Log($"P{p.index}: [{p.cid}] [{p.title}] [{BBDownUtil.FormatTime(p.dur)}]");
        }
        // 返回 session 由父流程在自身流内 Config.Apply：AsyncLocal 写入不会回流父调用方
        // （父流程的 ExecutionContext 快照在 await 前已捕获）。父流程在 GetVideoInfoAsync
        // 返回后继续调用 DownloadPagesAsync → Parser.WbiSign，必须用上这一版的凭据与新密钥，
        // 否则 w_rid 仍用旧密钥签名（密钥轮换后服务器会拒绝），本地凭据也会丢失。
        return (aidOri, vInfo, apiType, session);
    }

    /// <summary>
    /// 统一初始化一次请求会话：加载凭据（含本地 BBDown.data 等文件）→ 登录检查 → 提取 wbi。
    /// 返回完整的 <see cref="AppSettings"/>（含凭据与新 wbi），由调用方在自身异步流内
    /// Config.Apply 一次性应用——本方法不写 Config（AsyncLocal 写入只影响本方法上下文，
    /// 不会回流调用方）。CLI 下载、Serve 任务、订阅检查（SubCheck）、稍后再看（WatchLater）
    /// 都应先调用本方法并应用返回值，否则空间/收藏夹/合集等经 Parser.WbiSign 签名的请求
    /// 会用空 wbi 发出（B 站返回签名错误），本地凭据也会在返回后丢失。
    /// 检测到未登录时按凭据构成分流提示（见 <see cref="ClassifyLoginNotice"/>）：
    /// TV/APP/国际版模式已加载 access_token 时不再打印"你尚未登录…仅能试看 6 分钟"——
    /// nav 只验证网页 Cookie，不代表这些按 token 鉴权的模式不可用（实际会误报）。
    /// 返回 null 表示无需更新（如 INTL/TV 模式且未加载到新凭据）。
    /// </summary>
    public static async Task<AppSettings?> InitializeRequestSessionAsync(MyOption myOption, CancellationToken cancellationToken = default)
    {
        // 计算加载后的凭据（显式传参优先，否则本地文件），但不 Apply——
        // 由调用方拿返回值在自身流内应用，避免子方法内的 AsyncLocal 写入丢失。
        var (cookie, token) = await LoadCredentialsAsync(myOption, cancellationToken);

        // Cookie 即将过期的提前警告：B 站 SESSDATA 有效期约数月，serve 长驻进程跨周/月
        // 运行会静默失效，任务在运行中突然大面积鉴权失败。这里纯本地解析 SESSDATA
        // 估算剩余天数（无网络请求），低于阈值时提前提示扫码刷新。解析失败/未登录
        // 不警告（fail-open，见 EstimateSessdataExpiryDays）。
        if (!string.IsNullOrEmpty(cookie))
        {
            int? expiryDays = BBDownUtil.EstimateSessdataExpiryDays(cookie);
            if (expiryDays is not null && expiryDays < 30)
                Logger.LogWarn($"Cookie（SESSDATA）预计 {expiryDays} 天后过期，请提前运行 BBDown login 扫码刷新，以免任务鉴权失败");
        }

        string? newWbi = null;
        // 检测是否登录了账号并提取 wbi。WBI 是元数据 fetcher（SpaceVideoFetcher 的
        // mid: 列表）的签名依赖，不能只根据最终播放 API 模式决定：FetcherFactory 无论
        // TV/INTL/WEB 都把 mid: 路由到 SpaceVideoFetcher，后者无条件用 Parser.WbiSign
        // 签名（x/space/wbi/arc/search 是 WEB 接口）。因此只要可能解析空间类目标就初始化。
        if (Config.Current.Area == "")
        {
            Logger.Log("检测账号登录...");
            // CheckLoginWithDetails 内部经 HTTPUtil 读 Config.Current.Cookie（不消费传入的
            // cookie 参数），而本方法尚未把计算出的本地凭据写入当前流。若不清空旧值，用户
            // 显式传 --cookie 时 HTTPUtil 仍读 Config.Current.Cookie（可能是旧的/空的），
            // 导致登录检测用错误的凭据误报"Cookie 已过期"。这里把凭据应用到当前异步流
            // （只影响本方法上下文，不影响全局/父流），使检测使用正确凭据。
            Core.Config.ApplyToCurrentAsyncFlow(Config.Current with { Cookie = cookie, Token = token });
            var (isLoggedIn, cookieExpired, wbi) = await BBDownUtil.CheckLoginWithDetails(cookie, cancellationToken);
            newWbi = wbi;
            if (!isLoggedIn)
            {
                // nav 只验证网页 Cookie；TV/APP/国际版模式凭 access_token 鉴权
                // （Parser.GetPlayJsonAsync 按模式消费 Config.Current.Token）。已加载
                // token 时"未登录=只能试看 6 分钟"是误报——实际 playurl 完整成功；
                // 按提示类型分流（纯函数，各组合由 LoginNoticeClassificationTests 钉住）。
                var notice = ClassifyLoginNotice(
                    isLoggedIn,
                    cookieExpired,
                    tokenModeActive: myOption.UseTvApi || myOption.UseAppApi || myOption.UseIntlApi,
                    hasLoadedToken: !string.IsNullOrEmpty(token));
                if (notice == LoginNoticeKind.TokenMode)
                {
                    // 只陈述凭据构成，不声称"以此模式下载"：互动视频会在取到视频信息后
                    // 自动降级为默认下载（GetVideoInfoAsync 的 IsSteinGate 分支，届时
                    // 不再消费 token 并补打未登录横幅），断言实际走的接口会失准。
                    Logger.Log($"未检测到有效的网页登录（Cookie），已加载 {Parser.ApiModeLabel(Parser.ResolveApiMode(myOption.UseTvApi, myOption.UseIntlApi, myOption.UseAppApi))} 模式的 access_token。");
                }
                else if (notice == LoginNoticeKind.CookieExpired)
                {
                    Logger.LogWarn("========================================");
                    Logger.LogWarn("  Cookie 已过期！");
                    Logger.LogWarn("  请运行 BBDown login 重新扫码登录以获取新 Cookie。");
                    Logger.LogWarn("  或者使用 --use-tv-api 配合 --access-token 下载。");
                    Logger.LogWarn("  （若已执行 BBDown logintv，请加上 --use-tv-api）");
                    Logger.LogWarn("========================================");
                }
                else if (notice == LoginNoticeKind.NotLoggedIn)
                {
                    LogNotLoggedInBanner();
                }
            }
        }

        // 构造要应用到当前流的完整会话：含加载出的凭据（可能来自本地文件）与新 wbi。
        // 若与当前流配置无差异则返回 null，避免无谓的 Config.Apply。
        var current = Config.Current;
        var session = current with { Cookie = cookie, Token = token, Wbi = newWbi ?? current.Wbi };
        if (session == current) return null;
        return session;
    }

    /// <summary>登录提示类型（见 <see cref="ClassifyLoginNotice"/>）。</summary>
    internal enum LoginNoticeKind
    {
        /// <summary>已登录：不打印登录提示。</summary>
        None,
        /// <summary>TV/APP/国际版模式且已加载 access_token：Cookie 状态不影响下载，打印中性说明。</summary>
        TokenMode,
        /// <summary>本地持有 Cookie 但已失效：提示重新扫码。</summary>
        CookieExpired,
        /// <summary>无 Cookie 且无 token 模式可用：提示未登录（试看限制确实适用）。</summary>
        NotLoggedIn,
    }

    /// <summary>
    /// 未登录横幅（文案原样保留）。除登录检测的 NotLoggedIn 分支外，互动视频降级时
    /// （见 <see cref="GetVideoInfoAsync"/> 的 IsSteinGate 分支）复用——该场景下
    /// token 模式已不再适用，试看限制确实成立。
    /// </summary>
    private static void LogNotLoggedInBanner()
    {
        Logger.LogWarn("========================================");
        Logger.LogWarn("  你尚未登录B站账号！");
        Logger.LogWarn("  未登录状态下仅能下载6分钟试看片段。");
        Logger.LogWarn("  请运行 BBDown login 扫码登录以获取完整视频。");
        Logger.LogWarn("  （若已执行 BBDown logintv，请在下载命令中加上 --use-tv-api）");
        Logger.LogWarn("========================================");
    }

    /// <summary>
    /// 登录提示分类（纯函数，供单测直接覆盖各组合）：nav 接口只按 Cookie 判定登录，
    /// 不能代表 TV/APP/国际版模式的授权状态——这些模式按 access_token 鉴权，
    /// 已加载 token 时必须抑制"只能试看 6 分钟/请加 --use-tv-api"这类误报
    /// （命令已带 -t 时该建议也无意义）。token 与模式必须同时具备：
    /// 仅显式传 --access-token 而不开启 token 模式时，WEB 播放接口并不消费它。
    /// </summary>
    internal static LoginNoticeKind ClassifyLoginNotice(
        bool isLoggedIn, bool cookieExpired, bool tokenModeActive, bool hasLoadedToken)
        => isLoggedIn ? LoginNoticeKind.None
            : tokenModeActive && hasLoadedToken ? LoginNoticeKind.TokenMode
            : cookieExpired ? LoginNoticeKind.CookieExpired
            : LoginNoticeKind.NotLoggedIn;

}
