namespace BBDown.Tests;

/// <summary>
/// TV/APP/国际版模式"未登录"误报回归（tv 模式专项排查）：登录检查走 nav 接口，
/// 只按网页 Cookie 判定登录态；而 TV/APP/INTL 模式凭 access_token 鉴权
/// （Parser.GetPlayJsonAsync 按模式消费 Config.Current.Token）。本地已登录拿过
/// token 的用户（BBDownTV.data）以 -t 下载时，nav 必然返回未登录，此前无条件打印
/// "你尚未登录B站账号！未登录状态下仅能下载6分钟试看片段"——实际 playurl 完整成功。
/// 分类（<see cref="Program.ClassifyLoginNotice"/>）必须按"模式 + 已加载 token"组合分流：
/// 纯函数各组合在此钉住；nav 调用本身属真实网络行为（无单测覆盖），调用点接线由代码
/// 评审保障——判据必须用 LoadCredentialsAsync 返回的加载后 token，而非 myOption.AccessToken。
/// </summary>
public class LoginNoticeClassificationTests
{
    // InlineData 以 (int) 传递期望值：LoginNoticeKind 是 internal 枚举，
    // public 测试方法的参数不能用它（CS0051），方法内再转回枚举比较。
    [Theory]
    [InlineData(true, false, false, false, (int)Program.LoginNoticeKind.None)]        // 已登录
    [InlineData(true, true, true, true, (int)Program.LoginNoticeKind.None)]           // 已登录：任何凭据状态都不打登录提示
    [InlineData(false, false, true, true, (int)Program.LoginNoticeKind.TokenMode)]    // 误报主场景：TV 模式 + 本地 token，无 Cookie
    [InlineData(false, true, true, true, (int)Program.LoginNoticeKind.TokenMode)]     // 旧 Cookie 失效 + 有效 TV token：不得再提示"请重新扫码/加 -t"
    [InlineData(false, false, true, false, (int)Program.LoginNoticeKind.NotLoggedIn)] // -t 但未加载到 token：试看限制确实适用，维持原横幅
    [InlineData(false, false, false, true, (int)Program.LoginNoticeKind.NotLoggedIn)] // 有 token 但未开 token 模式：WEB 播放接口不消费它，不得抑制
    [InlineData(false, true, false, false, (int)Program.LoginNoticeKind.CookieExpired)]
    [InlineData(false, false, false, false, (int)Program.LoginNoticeKind.NotLoggedIn)]
    public void ClassifyLoginNotice_ByCredentialCombination(
        bool isLoggedIn, bool cookieExpired, bool tokenModeActive, bool hasLoadedToken, int expected)
    {
        Assert.Equal((Program.LoginNoticeKind)expected,
            Program.ClassifyLoginNotice(isLoggedIn, cookieExpired, tokenModeActive, hasLoadedToken));
    }
}
