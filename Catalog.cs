namespace ApexF1;

public sealed record SourceLink(
    string Title,
    string Description,
    string Provider,
    string Address,
    string Glyph,
    string Keywords = "",
    Uri? ImageUri = null)
{
    public Uri Uri { get; } = new(Address);
    public string DisplayTitle => UiLanguage.Translate(Title);
    public string DisplayDescription => UiLanguage.Translate(Description);
    public string DisplayProvider => UiLanguage.Translate(Provider);

    public override string ToString() => DisplayTitle;
}

public sealed record LinkGroup(string Title, IReadOnlyList<SourceLink> Links)
{
    public string DisplayTitle => UiLanguage.Translate(Title);
}

internal static class Catalog
{
    private const string F1 = "https://www.formula1.com";
    private static readonly int Season = DateTime.Now.Year;

    private static SourceLink Official(string title, string description, string path, string glyph, string keywords = "")
        => new(title, description, "Formula 1", path.StartsWith('/') ? F1 + path : path, glyph, keywords);

    private static SourceLink Cctv(string title, string description, string address, string glyph, string keywords = "")
        => new(title, description, "央视网", address, glyph, keywords);

    internal static readonly SourceLink F1Home = Official("F1 官网首页", "官方头条、精选内容和完整站点导航。", "/en", "\uE774", "官网 主页 首页 Formula1");
    internal static readonly SourceLink News = Official("最新资讯", "围场新闻、专访和官方分析。", "/en/latest", "\uE7C3", "新闻 news");
    internal static readonly SourceLink Calendar = Official("赛历与赛程", "各站大奖赛日期、赛道和分节时间。", $"/en/racing/{Season}", "\uE787", "时间 正赛 排位 冲刺 练习 schedule calendar");
    internal static readonly SourceLink Results = Official("比赛成绩", "分站赛果和历年成绩档案。", $"/en/results/{Season}/races", "\uE9D9", "历史 冠军 results");
    internal static readonly SourceLink Standings = Official("车手积分", "世界车手锦标赛官方积分榜。", $"/en/results/{Season}/drivers", "\uECA5", "排名 车手积分榜 standings");
    internal static readonly SourceLink TeamStandings = Official("车队积分", "世界车队锦标赛官方积分榜。", $"/en/results/{Season}/team", "\uECA5", "车队排名 constructors standings");
    internal static readonly SourceLink Video = Official("官方视频", "比赛集锦、车载镜头和围场节目。", "/en/video", "\uE714", "回放 精彩瞬间 video highlights");
    internal static readonly SourceLink Timing = Official("实时计时", "比赛排名、赛段、圈速和赛道追踪。", "/en/timing/f1-live", "\uE916", "数据 遥测 live timing");
    internal static readonly SourceLink F1Tv = Official("F1 TV", "官方赛事直播、回放、节目和历史档案。", "https://f1tv.formula1.com/", "\uE7F4", "直播 录像 观赛");
    internal static readonly SourceLink CctvRacing = Cctv("央视赛车专区", "F1 转播预告、比赛视频和赛车报道。", "https://sports.cctv.com/racing/", "\uE7C3", "CCTV F1 直播 中国大奖赛");
    internal static readonly SourceLink Cctv5 = Cctv("CCTV-5 体育", "打开央视体育频道官方直播页。", "https://tv.cctv.com/live/cctv5/", "\uE7F4", "央视 五套 cctv5 直播");
    internal static readonly SourceLink Cctv5Plus = Cctv("CCTV-5+ 体育赛事", "打开央视体育赛事频道官方直播页。", "https://tv.cctv.com/live/cctv5plus/", "\uE7F4", "央视 五加 cctv5plus 直播");
    internal static readonly SourceLink BiliNews = new("F1赛事资讯", "赛事战报、赛前前瞻和围场资讯。", "哔哩哔哩", "https://space.bilibili.com/65125803", "\uE7C3", "B站 bilibili F1赛事咨询 UP主");
    internal static readonly SourceLink BiliVideos = new(BiliNews.Title, BiliNews.Description, BiliNews.Provider,
        BiliNews.Address + "/video", "\uE714");
    internal static readonly SourceLink Masha = new("马莎拉车", "胡闹 P 房的赛事解说和车迷内容。", "哔哩哔哩", "https://space.bilibili.com/1313650809", "\uE8BD", "B站 bilibili 玛莎拉车 马沙拉车 UP主");
    internal static readonly SourceLink MashaLive = new("马莎拉车 · 数据直播间", "查看 B 站直播间的开播状态和赛事数据解说。", "哔哩哔哩", "https://live.bilibili.com/23954427", "\uEC15", "B站 bilibili 胡闹P房");

    internal static readonly LinkGroup[] OfficialSite =
    [
        new("资讯与赛季",
        [
            F1Home, News, Calendar, Results, Standings,
            TeamStandings,
            Official("车手", "现役车手名单、个人档案与生涯数据。", "/en/drivers", "\uE77B", "drivers"),
            Official("车队", "各支车队的阵容与历史。", "/en/teams", "\uE902", "teams"),
            Official("F1 奖项", "查看最佳车手、最快圈和其他官方奖项。", "/en/results/awards", "\uE735", "awards"),
            Official("F1 入门", "了解比赛周末、排位赛、冲刺赛和计分规则。", "/en/page/what-is-f1", "\uE82D", "规则 新手 指南")
        ]),
        new("视频、计时与互动",
        [
            Video, Timing, F1Tv,
            Official("F1 TV 订阅", "查看当地可用的 F1 TV 服务与订阅。", "/subscribe-to-f1-tv", "\uE719"),
            Official("全球转播商", "按国家或地区查看官方 F1 转播商。", "/en/information/f1-broadcast-information.45y3LNsT1D6VoK0ZmX8ciJ", "\uE909", "直播 观赛 国内 中国 转播权"),
            Official("F1 Unlocked", "官方会员内容、比赛和奖励。", "/en/page/unlocked", "\uE785"),
            Official("F1 Fantasy", "组建你的虚拟车队并参与赛季排名。", "https://fantasy.formula1.com/en/", "\uE7FC"),
            Official("F1 游戏", "了解 Formula 1 官方游戏和模拟驾驶。", "/en/page/gaming", "\uE7FC"),
            Official("Beyond The Grid", "F1 官方访谈播客及相关文章。", "/en/latest/tags/beyond-the-grid.63HGi6Q0grEg1ToZBtPNQ9", "\uE720", "podcast 播客"),
            Official("F1 Fan Voice", "参与 F1 官方车迷社区与调查。", "https://www.f1fanvoice.com/auth/sign_in", "\uE716")
        ]),
        new("门票、体验与商店",
        [
            Official("官方门票", "查看大奖赛门票和场地选择。", "https://ticketing.formula1.com/seasons/", "\uE8C8"),
            Official("赛事款待", "查看围场俱乐部和贵宾观赛服务。", "https://ticketing.formula1.com/hospitality/seasons/", "\uE734"),
            Official("F1 Experiences", "查看观赛旅行套餐和赛道体验。", "https://f1experiences.com/", "\uE707"),
            Official("F1 Arcade", "探索 F1 模拟赛车场馆。", "https://f1arcade.com/", "\uE7FC"),
            Official("F1 Store", "浏览官方服饰与车队周边。", "https://f1store.formula1.com/en/", "\uE719"),
            Official("F1 Authentics", "浏览官方赛车收藏品。", "https://www.f1authentics.com/", "\uE7B8")
        ]),
        new("账户、支持与企业",
        [
            Official("F1 账户", "登录 Formula 1 账户并管理个人资料。", "https://account.formula1.com/#/en/login?redirect=https%3A%2F%2Fwww.formula1.com%2Fen&lead_source=web_f1core&ch=COWB", "\uE77B"),
            Official("F1 帮助中心", "查询账户、F1 TV 和订阅相关帮助。", "https://f1help.formula1.com/?language=en_US", "\uE897"),
            Official("F1 合作伙伴", "查看官方赞助商和供应商。", "/en/information/formula-1-partners.2shz7bIVhGgS3ovP69N3Rs", "\uE902"),
            Official("F1 企业官网", "公司信息、媒体发布和可持续发展。", "https://corp.formula1.com/", "\uE821")
        ]),
        new("条款与内容指南",
        [
            Official("法律声明", "浏览 Formula 1 官网法律信息。", "/en/information/legal-notices.7egvZU48hzrypubGBNcQKt", "\uE8A5"),
            Official("内容使用指南", "了解官网内容和知识产权使用指引。", "/en/information/guidelines.4EOKE9RRqevL4niTK9kWyt", "\uE82D"),
            Official("使用条款", "查看 Formula 1 官网服务条款。", "https://account.formula1.com/#/en/terms-of-use", "\uE8A5"),
            Official("隐私政策", "查看 Formula 1 的个人信息处理政策。", "https://account.formula1.com/#/en/privacy-policy", "\uE72E"),
            Official("Cookie 政策", "查看官网使用 Cookie 的方式。", "https://account.formula1.com/#/en/cookie-policy/", "\uE8A5"),
            Official("无障碍声明", "查看 Formula 1 官网的无障碍说明。", "https://account.formula1.com/#/en/f1-web-accessibility-statement", "\uE776")
        ])
    ];

    internal static readonly LinkGroup[] Watch =
    [
        new("央视观赛",
        [
            CctvRacing, Cctv5, Cctv5Plus,
            Cctv("体育节目单", "核对 CCTV-5、CCTV-5+ 的播出安排。", "https://sports.cctv.com/epg/?channel=cctv5+", "\uE787", "央视 转播时间 排期"),
            Cctv("中国大奖赛转播专题", "央视网发布的 2026 中国站冲刺赛、排位赛和正赛视频入口。", "https://sports.cctv.com/2026/03/13/ARTIlAw71VszUnr9U11BxoaU260313.shtml", "\uE714", "上海 回看 直播 中国站")
        ]),
        new("官方直播与数据", [F1Tv, Timing, Video])
    ];

    internal static readonly LinkGroup[] Creators =
    [
        new("哔哩哔哩", [BiliNews, Masha, MashaLive])
    ];

    internal static readonly SourceLink[] All = OfficialSite.Concat(Watch).Concat(Creators)
        .SelectMany(group => group.Links)
        .DistinctBy(link => link.Address)
        .ToArray();

    internal static IEnumerable<SourceLink> Search(string query)
    {
        var terms = query.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return terms.Length == 0 ? [] : All
            .Where(link => terms.All(term => Contains($"{link.Title} {link.DisplayTitle} {link.Description} {link.DisplayDescription} {link.Provider} {link.DisplayProvider} {link.Keywords}", term)))
            .OrderByDescending(link => Contains(link.DisplayTitle, query.Trim()) || Contains(link.Title, query.Trim()));
    }

    private static bool Contains(string value, string term) => value.Contains(term, StringComparison.OrdinalIgnoreCase);
}
