using Spectre.Console.Cli;
using System.ComponentModel;
using System.Diagnostics.CodeAnalysis;

namespace BBDown.Commands;

/// <summary>
/// 需要"下载能力"的子命令（<c>watchlater</c> / <c>sub check</c>）共用的 8 个下载选项（I9）。
/// 两个 Settings 类此前逐字复制这 8 个属性（含选项名与描述），漂移风险在于：改了一处、
/// 另一处静默保持旧语义，而 CLI 帮助与绑定行为就此分叉。
///
/// 约束：属性名、<see cref="CommandOptionAttribute"/> 与 <see cref="DescriptionAttribute"/>
/// 的文本都必须与拆分前逐字一致——它们是 Spectre 绑定与 CLI 契约的来源（REFACTOR_PLAN §4 #1）。
/// 刻意声明为 <c>abstract</c>：<c>SettingsTypeCatalog</c> 反射枚举"非抽象 <see cref="CommandSettings"/>
/// 派生类型"作为 CLI Settings 全集，抽象基类不参与该集合（否则会被要求进 CliOptionIndex 与
/// Program.Main 的 AOT root 清单）；派生类型经 <c>GetProperties</c> 仍能拿到这些继承选项。
///
/// **为什么派生自 <see cref="SubSettings"/>**：Spectre 的分支注册
/// （<c>AddBranch&lt;SubSettings&gt;</c> → <c>AddCommand&lt;TCommand&gt;</c>）把命令约束为
/// <c>ICommandLimiter&lt;SubSettings&gt;</c>，而该接口对 Settings 协变——<c>sub check</c> 的
/// Settings 必须是 <see cref="SubSettings"/> 的派生类型，否则注册点无法通过编译。把共用选项放在
/// <see cref="SubSettings"/> 自身会让 <c>sub add/list/remove</c> 也悄悄接受这 8 个选项（CLI 面变化），
/// 因此放在这一中间层：三个不需要下载能力的子命令仍直接派生 <see cref="SubSettings"/>，CLI 面不变；
/// <c>watchlater</c> 复用同一选项集，其层级归属只是约束的副作用，无行为含义。
/// </summary>
[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicConstructors)]
public abstract class DownloadOptionSettings : SubSettings
{
    [CommandOption("-c|--cookie")]
    [Description("Cookie 字符串")]
    public string Cookie { get; set; } = "";

    [CommandOption("--access-token")]
    [Description("access token")]
    public string AccessToken { get; set; } = "";

    [CommandOption("-e|--encoding-priority")]
    [Description("视频编码优先级, 如 hevc,avc,av1")]
    public string? EncodingPriority { get; set; }

    [CommandOption("-q|--dfn-priority")]
    [Description("视频清晰度优先级, 如 8K 4K 1080P 高清 720P 高清")]
    public string? DfnPriority { get; set; }

    [CommandOption("-a|--use-app-api")]
    [Description("使用APP端解析模式")]
    public bool UseAppApi { get; set; }

    [CommandOption("-t|--use-tv-api")]
    [Description("使用TV端解析模式")]
    public bool UseTvApi { get; set; }

    [CommandOption("--use-intl-api")]
    [Description("使用国际版解析模式")]
    public bool UseIntlApi { get; set; }

    [CommandOption("-w|--work-dir")]
    [Description("设置工作目录(所有相对路径的根目录; 默认: 程序目录下的 Download 文件夹)")]
    public string WorkDir { get; set; } = "";

    /// <summary>
    /// 把共用选项映射为 <see cref="MyOption"/>。取代两份逐字复制的 <c>BuildOption</c>（I9）：
    /// <paramref name="url"/> 是本次要下载的目标，<paramref name="workDir"/> 由调用方按上下文给出
    /// （watchlater 传已绝对化的 <see cref="WorkDir"/>；sub check 传按 <c>--per-sub-dir</c>
    /// 附加了订阅子目录的目录），因此不在这里自行解析。
    /// </summary>
    internal MyOption ToMyOption(string url, string workDir) => new()
    {
        Url = url,
        Cookie = Cookie,
        AccessToken = AccessToken,
        EncodingPriority = EncodingPriority,
        DfnPriority = DfnPriority,
        UseAppApi = UseAppApi,
        UseTvApi = UseTvApi,
        UseIntlApi = UseIntlApi,
        WorkDir = workDir,
    };
}
