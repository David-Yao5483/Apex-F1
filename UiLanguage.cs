using System.Globalization;

namespace ApexF1;

internal static class UiLanguage
{
    internal static bool English { get; set; }

    internal static bool UseEnglish(string? saved, CultureInfo systemLanguage) => saved switch
    {
        "en" => true,
        "zh-CN" => false,
        _ => systemLanguage.TwoLetterISOLanguageName != "zh"
    };

    internal static string Translate(string text) =>
        English && EnglishText.TryGetValue(text, out var translation) ? translation : text;

    internal static string Choose(string chinese, string english) => English ? english : chinese;

    internal static string SessionName(string name) => English ? name : name switch
    {
        "Practice 1" => "第一次练习赛",
        "Practice 2" => "第二次练习赛",
        "Practice 3" => "第三次练习赛",
        "Sprint Qualifying" => "冲刺排位赛",
        "Sprint" => "冲刺赛",
        "Qualifying" => "排位赛",
        "Race" => "正赛",
        "Day 1" => "测试第 1 天",
        "Day 2" => "测试第 2 天",
        "Day 3" => "测试第 3 天",
        _ => name
    };

    private static readonly Dictionary<string, string> EnglishText = new()
    {
        ["新闻"] = "News",
        ["积分榜"] = "Standings",
        ["赛果"] = "Results",
        ["赛历"] = "Calendar",
        ["观赛"] = "Watch",
        ["官方赛果"] = "Official results",
        ["官方赛历"] = "Official calendar",
        ["数据来源"] = "Data source",
        ["F1 官网新闻"] = "Formula 1 news",
        ["F1赛事资讯 · 最新视频"] = "F1 Race News · Latest videos",
        ["F1赛事资讯"] = "F1 Race News",
        ["哔哩哔哩"] = "Bilibili",
        ["央视网"] = "CCTV",
        ["央视观赛"] = "Watch on CCTV",
        ["官方直播与数据"] = "Official streams and timing",
        ["搜索结果"] = "Search results",
        ["相关入口"] = "Matching links",
        ["浏览 Formula 1 官网新闻和 F1赛事资讯的近期视频。"] = "Read Formula 1 news and watch recent videos from F1 Race News.",
        ["查看本赛季 Formula 1 官方积分。"] = "View the official Formula 1 standings for this season.",
        ["查看本赛季各站的正赛冠军、车队、圈数和完赛时间。"] = "Browse this season’s Grand Prix winners, teams, lap counts and finishing times.",
        ["查看各站大奖赛、赛道和分节时间。"] = "Browse Grands Prix, circuits and session times.",
        ["央视赛事转播以节目单为准。F1 TV 的直播和回放取决于所在地区及订阅。"] = "Check CCTV listings for broadcast times. F1 TV availability depends on your region and subscription.",
        ["F1 官网首页"] = "Formula 1 homepage",
        ["官方头条、精选内容和完整站点导航。"] = "Official headlines, featured stories and site navigation.",
        ["最新资讯"] = "Latest news",
        ["围场新闻、专访和官方分析。"] = "Paddock news, interviews and official analysis.",
        ["赛历与赛程"] = "Race calendar",
        ["各站大奖赛日期、赛道和分节时间。"] = "Grand Prix dates, circuits and session times.",
        ["比赛成绩"] = "Race results",
        ["分站赛果和历年成绩档案。"] = "Grand Prix results and historical records.",
        ["车手积分"] = "Driver standings",
        ["世界车手锦标赛官方积分榜。"] = "Official World Drivers' Championship standings.",
        ["车队积分"] = "Team standings",
        ["世界车队锦标赛官方积分榜。"] = "Official World Constructors' Championship standings.",
        ["官方视频"] = "Official videos",
        ["比赛集锦、车载镜头和围场节目。"] = "Race highlights, onboards and paddock shows.",
        ["实时计时"] = "Live timing",
        ["比赛排名、赛段、圈速和赛道追踪。"] = "Positions, sectors, lap times and track maps.",
        ["官方赛事直播、回放、节目和历史档案。"] = "Official live sessions, replays, shows and archives.",
        ["央视赛车专区"] = "CCTV Motorsport",
        ["F1 转播预告、比赛视频和赛车报道。"] = "F1 broadcast listings, race videos and motorsport coverage.",
        ["CCTV-5 体育"] = "CCTV-5 Sports",
        ["打开央视体育频道官方直播页。"] = "Open the official CCTV sports live channel.",
        ["CCTV-5+ 体育赛事"] = "CCTV-5+ Sports Events",
        ["打开央视体育赛事频道官方直播页。"] = "Open the official CCTV sports events live channel.",
        ["赛事战报、赛前前瞻和围场资讯。"] = "Race reports, previews and paddock news.",
        ["马莎拉车"] = "Masha Racing",
        ["胡闹 P 房的赛事解说和车迷内容。"] = "Race commentary and fan coverage from HNPF.",
        ["马莎拉车 · 数据直播间"] = "Masha Racing · Live data",
        ["查看 B 站直播间的开播状态和赛事数据解说。"] = "Watch the Bilibili live room for timing and commentary.",
        ["体育节目单"] = "Sports schedule",
        ["核对 CCTV-5、CCTV-5+ 的播出安排。"] = "Check the CCTV-5 and CCTV-5+ broadcast schedule.",
        ["中国大奖赛转播专题"] = "Chinese Grand Prix coverage",
        ["央视网发布的 2026 中国站冲刺赛、排位赛和正赛视频入口。"] = "CCTV coverage of the 2026 Chinese Grand Prix sprint, qualifying and race.",
        ["车手"] = "Drivers",
        ["现役车手名单、个人档案与生涯数据。"] = "Current drivers, biographies and career statistics.",
        ["车队"] = "Teams",
        ["各支车队的阵容与历史。"] = "Team lineups and history.",
        ["F1 奖项"] = "F1 awards",
        ["查看最佳车手、最快圈和其他官方奖项。"] = "Driver of the Day, fastest lap and other official awards.",
        ["F1 入门"] = "New to F1?",
        ["了解比赛周末、排位赛、冲刺赛和计分规则。"] = "Learn about race weekends, qualifying, sprints and scoring.",
        ["F1 TV 订阅"] = "F1 TV subscription",
        ["查看当地可用的 F1 TV 服务与订阅。"] = "Check F1 TV plans available in your region.",
        ["全球转播商"] = "Global broadcasters",
        ["按国家或地区查看官方 F1 转播商。"] = "Find official F1 broadcasters by country or region.",
        ["官方会员内容、比赛和奖励。"] = "Official member content, competitions and rewards.",
        ["组建你的虚拟车队并参与赛季排名。"] = "Build your fantasy team and compete throughout the season.",
        ["F1 游戏"] = "F1 games",
        ["了解 Formula 1 官方游戏和模拟驾驶。"] = "Explore official Formula 1 games and sim racing.",
        ["F1 官方访谈播客及相关文章。"] = "Official Formula 1 interview podcast and related stories.",
        ["参与 F1 官方车迷社区与调查。"] = "Join the official fan community and surveys.",
        ["官方门票"] = "Official tickets",
        ["查看大奖赛门票和场地选择。"] = "Browse Grand Prix tickets and venue options.",
        ["赛事款待"] = "Hospitality",
        ["查看围场俱乐部和贵宾观赛服务。"] = "Explore Paddock Club and premium hospitality.",
        ["查看观赛旅行套餐和赛道体验。"] = "Browse travel packages and circuit experiences.",
        ["探索 F1 模拟赛车场馆。"] = "Explore Formula 1 simulator venues.",
        ["浏览官方服饰与车队周边。"] = "Browse official apparel and team merchandise.",
        ["浏览官方赛车收藏品。"] = "Browse official racing memorabilia.",
        ["F1 账户"] = "F1 account",
        ["登录 Formula 1 账户并管理个人资料。"] = "Sign in and manage your Formula 1 account.",
        ["F1 帮助中心"] = "F1 Help Centre",
        ["查询账户、F1 TV 和订阅相关帮助。"] = "Get help with accounts, F1 TV and subscriptions.",
        ["F1 合作伙伴"] = "F1 partners",
        ["查看官方赞助商和供应商。"] = "Explore official sponsors and suppliers.",
        ["F1 企业官网"] = "F1 corporate site",
        ["公司信息、媒体发布和可持续发展。"] = "Corporate information, press releases and sustainability.",
        ["法律声明"] = "Legal notices",
        ["浏览 Formula 1 官网法律信息。"] = "Read Formula 1 website legal notices.",
        ["内容使用指南"] = "Content guidelines",
        ["了解官网内容和知识产权使用指引。"] = "Read guidance on content and intellectual property.",
        ["使用条款"] = "Terms of use",
        ["查看 Formula 1 官网服务条款。"] = "Read the Formula 1 website terms.",
        ["隐私政策"] = "Privacy policy",
        ["查看 Formula 1 的个人信息处理政策。"] = "Read how Formula 1 handles personal information.",
        ["Cookie 政策"] = "Cookie policy",
        ["查看官网使用 Cookie 的方式。"] = "Read how the official website uses cookies.",
        ["无障碍声明"] = "Accessibility statement",
        ["查看 Formula 1 官网的无障碍说明。"] = "Read the official website accessibility statement.",
        ["在应用内阅读原文"] = "Read the article in the app",
        ["在应用内观看"] = "Watch in the app"
    };
}
