using System.ComponentModel;
using System.Reflection;
using BBDown.Commands;
using Spectre.Console.Cli;

namespace BBDown.Tests;

/// <summary>
/// I9：<c>watchlater</c> / <c>sub check</c> 的 8 个下载选项收敛到 <see cref="DownloadOptionSettings"/>。
/// 本类钉住四件事：
/// ① 这 8 个选项只在基类声明**一处**（派生类不得再复制副本）；
/// ② 两个命令看到的选项名（长/短）与描述逐字一致；
/// ③ 不需要下载能力的 <c>sub add/list/remove</c> **不得**继承它们——把选项放进
///    <see cref="SubSettings"/> 会让这三个命令悄悄接受 8 个无效选项（CLI 面变化）；
/// ④ 在 Spectre 分支约束（<c>ICommandLimiter&lt;SubSettings&gt;</c>，对 Settings 协变）下，
///    真实 <see cref="CommandApp"/> 仍能把继承选项绑定到位、并映射进 <see cref="MyOption"/>。
/// </summary>
public class DownloadOptionSettingsTests
{
    /// <summary>两个命令共用的 8 个下载选项（属性名）。</summary>
    private static readonly string[] SharedOptionNames =
        ["Cookie", "AccessToken", "EncodingPriority", "DfnPriority", "UseAppApi", "UseTvApi", "UseIntlApi", "WorkDir"];

    /// <summary>
    /// CLI 契约快照：I9 收敛前后必须逐字一致的选项名（短名|长名）与描述文本。
    /// 硬编码而非"两命令互比"——互比在两命令都继承同一属性时恒真，改坏基类描述也发现不了。
    /// </summary>
    private static readonly Dictionary<string, (string Tokens, string Description)> ExpectedSharedOptions = new(StringComparer.Ordinal)
    {
        ["Cookie"] = ("-c|--cookie", "Cookie 字符串"),
        ["AccessToken"] = ("|--access-token", "access token"),
        ["EncodingPriority"] = ("-e|--encoding-priority", "视频编码优先级, 如 hevc,avc,av1"),
        ["DfnPriority"] = ("-q|--dfn-priority", "视频清晰度优先级, 如 8K 4K 1080P 高清 720P 高清"),
        ["UseAppApi"] = ("-a|--use-app-api", "使用APP端解析模式"),
        ["UseTvApi"] = ("-t|--use-tv-api", "使用TV端解析模式"),
        ["UseIntlApi"] = ("|--use-intl-api", "使用国际版解析模式"),
        ["WorkDir"] = ("-w|--work-dir", "设置工作目录(所有相对路径的根目录; 默认: 程序目录下的 Download 文件夹)"),
    };

    private static readonly Type[] DownloadCommands = [typeof(WatchLaterSettings), typeof(SubCheckSettings)];

    /// <summary>① 声明处唯一：属性必须由基类声明，而不是各命令各自复制的同名副本。</summary>
    [Fact]
    public void SharedOptions_AreDeclaredOnceOnTheBaseClass()
    {
        foreach (var type in DownloadCommands)
        {
            foreach (var name in SharedOptionNames)
            {
                var prop = type.GetProperty(name);
                Assert.NotNull(prop);
                Assert.Equal(typeof(DownloadOptionSettings), prop!.DeclaringType);
            }
        }
    }

    /// <summary>② 两个命令看到的选项名与描述必须与拆分前逐字一致（硬编码契约快照）。</summary>
    [Fact]
    public void SharedOptions_KeepTheirCliContract()
    {
        foreach (var type in DownloadCommands)
        {
            foreach (var name in SharedOptionNames)
            {
                var prop = type.GetProperty(name)!;
                var option = prop.GetCustomAttribute<CommandOptionAttribute>();
                Assert.NotNull(option);
                var description = prop.GetCustomAttribute<DescriptionAttribute>()?.Description ?? "";
                var tokens = string.Join(",", option!.ShortNames.Select(n => "-" + n))
                    + "|" + string.Join(",", option.LongNames.Select(n => "--" + n));

                var expected = ExpectedSharedOptions[name];
                Assert.Equal(expected.Tokens, tokens);
                Assert.Equal(expected.Description, description);
            }
        }

        // 快照不得漏项：属性名清单与期望表必须一一对应
        Assert.Equal(SharedOptionNames.OrderBy(n => n, StringComparer.Ordinal),
            ExpectedSharedOptions.Keys.OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>③ 其余 sub 命令不暴露这些选项（选项集若不放在中间层而放进 SubSettings，本用例失败）。</summary>
    [Fact]
    public void SubCommandsWithoutDownloadCapability_DoNotExposeDownloadOptions()
    {
        Type[] others = [typeof(SubAddSettings), typeof(SubListSettings), typeof(SubRemoveSettings)];
        foreach (var type in others)
        {
            foreach (var name in SharedOptionNames)
                Assert.Null(type.GetProperty(name));
        }
    }

    /// <summary>④ 驱动真实 CommandApp：分支下的命令能绑定基类选项与自身选项。</summary>
    [Fact]
    public void BranchCommand_BindsInheritedAndOwnOptions()
    {
        CaptureCheckCommand.Captured = null;
        var app = new CommandApp();
        app.Configure(config => config.AddBranch<SubSettings>("sub", sub =>
            sub.AddCommand<CaptureCheckCommand>("check")));

        var exit = app.Run([
            "sub", "check",
            "--cookie", "c1", "--access-token", "t1", "-e", "hevc", "-q", "8K",
            "-a", "-t", "--use-intl-api", "-w", "wd1",
            "--per-sub-dir", "--full-scan",
        ]);

        Assert.Equal(0, exit);
        var s = CaptureCheckCommand.Captured!;
        Assert.Equal("c1", s.Cookie);
        Assert.Equal("t1", s.AccessToken);
        Assert.Equal("hevc", s.EncodingPriority);
        Assert.Equal("8K", s.DfnPriority);
        Assert.True(s.UseAppApi);
        Assert.True(s.UseTvApi);
        Assert.True(s.UseIntlApi);
        Assert.Equal("wd1", s.WorkDir);
        Assert.True(s.PerSubDir);
        Assert.True(s.FullScan);
    }

    /// <summary>④ 两个命令共用同一份映射：8 个选项 + url/workDir 全部落到 MyOption。</summary>
    [Fact]
    public void ToMyOption_MapsEverySharedOption_ForBothCommands()
    {
        var watchLater = new WatchLaterSettings
        {
            Cookie = "c",
            AccessToken = "t",
            EncodingPriority = "hevc",
            DfnPriority = "8K",
            UseAppApi = true,
            UseTvApi = true,
            UseIntlApi = true,
            WorkDir = "w",
        };
        var check = new SubCheckSettings
        {
            Cookie = "c",
            AccessToken = "t",
            EncodingPriority = "hevc",
            DfnPriority = "8K",
            UseAppApi = true,
            UseTvApi = true,
            UseIntlApi = true,
            WorkDir = "w",
        };

        foreach (var opt in new[] { watchLater.ToMyOption("av1", "wd"), check.ToMyOption("av1", "wd") })
        {
            Assert.Equal("av1", opt.Url);
            Assert.Equal("c", opt.Cookie);
            Assert.Equal("t", opt.AccessToken);
            Assert.Equal("hevc", opt.EncodingPriority);
            Assert.Equal("8K", opt.DfnPriority);
            Assert.True(opt.UseAppApi);
            Assert.True(opt.UseTvApi);
            Assert.True(opt.UseIntlApi);
            Assert.Equal("wd", opt.WorkDir);
        }
    }

    /// <summary>捕获绑定结果的假命令（不执行任何下载）——只替代 SubCheckCommand 的入口。</summary>
    private sealed class CaptureCheckCommand : AsyncCommand<SubCheckSettings>
    {
        public static SubCheckSettings? Captured;

        protected override Task<int> ExecuteAsync(CommandContext context, SubCheckSettings settings, CancellationToken cancellationToken)
        {
            Captured = settings;
            return Task.FromResult(0);
        }
    }
}
