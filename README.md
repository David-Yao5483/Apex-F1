# Apex F1

Apex F1 是一个 Windows 桌面观赛与赛事信息应用，使用 C#、WinUI 3 XAML 和 Windows App SDK。窗口顶部居中排列新闻、积分榜、赛果、赛历和观赛五个标签，启动时打开新闻。首次启动时，中文系统使用简体中文，其余系统语言使用英文。标题栏左侧可切换中英文和深浅主题，应用会记住选择。深浅切换按钮右侧的刷新图标用于更新当前数据或重载应用内网页。

首次启动默认最大化。应用关闭时记住最大化或普通窗口状态，以及普通窗口的大小。

## 构建与运行

本机生成的 x64 发布目录为 `bin\publish\win-x64-current`。打开其中的 `ApexF1.exe` 即可运行。该版本使用系统中的 .NET 10 运行时和 WebView2 Runtime。

在 Windows 上安装 [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) 和 [Microsoft Edge WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)。使用支持 Windows App SDK 的 Visual Studio 打开项目，或在项目目录运行以下命令。

```powershell
dotnet build .\ApexF1.csproj -p:Platform=x64
.\bin\x64\Debug\net10.0-windows10.0.26100.0\win-x64\ApexF1.exe
```

发布可复制到另一台 Windows 电脑运行的 x64 目录。请保留输出目录内的所有文件。

```powershell
dotnet publish .\ApexF1.csproj -c Release -p:Platform=x64 -r win-x64 --self-contained true -o .\bin\publish\win-x64-portable
```

ARM64 使用 `-p:Platform=ARM64 -r win-arm64`。应用采用免安装目录分发，随应用部署 Windows App SDK，网页阅读和观看仍需要 WebView2 Runtime。

## 内容与观赛

- **新闻**显示最近六篇 F1 官网文章和最近六条「F1赛事资讯」公开合集视频。两个区域均显示封面和标题，每行三张卡片。点击卡片在应用内打开原文或视频，区域左下方的「阅读更多」和「观看更多」通往对应列表。[F1 官网新闻](https://www.formula1.com/en/latest) · [F1赛事资讯](https://space.bilibili.com/65125803)
- **积分榜**以原生 WinUI 表格显示名次、车手、国籍、车队及积分。车手肖像和圆形队徽取自 F1 官网。将鼠标悬停在车队积分榜的队徽上可查看 2026 赛季领队资料。[车手积分榜](https://www.formula1.com/en/results/2026/drivers) · [2026 车队领队](https://www.formula1.com/en/latest/article/who-are-the-2026-team-principals.2Jrw5LEJbEoX2gM5OtFL8K)
- **赛果和赛历**直接显示本赛季分站正赛冠军、车队、圈数、完赛时间，以及各站比赛周末的日期。赛果中的分站打开七列原生完赛明细表，赛历中的分站用原生表格显示各比赛环节的 Windows 本地时间。[官方赛果](https://www.formula1.com/en/results/2026/races) · [官方赛历](https://www.formula1.com/en/racing/2026)
- **观赛**提供央视赛车专区、体育节目单、CCTV-5 和 CCTV-5+ 官方直播页及央视中国大奖赛转播专题。央视转播场次以节目单和赛事预告为准。[央视赛车专区](https://sports.cctv.com/racing/) · [体育节目单](https://sports.cctv.com/epg/?channel=cctv5+)
- **创作者**可直达两个 UP 主的空间和马莎拉车的数据直播间。[F1赛事资讯](https://space.bilibili.com/65125803) · [马莎拉车](https://space.bilibili.com/1313650809) · [数据直播间](https://live.bilibili.com/23954427)

车手及领队肖像、新闻封面和视频封面缓存在 `%LocalAppData%\ApexF1\ImageCache`。图片缓存有效期为 30 天，超过 128 MB 时会清理较早缓存的文件。

使用 `Ctrl+F` 可定位标题栏搜索框。分站赛果和应用内网页使用标题栏左侧的同一个回退按钮。应用内网页支持“在浏览器打开”。播放、登录和订阅条件由各来源平台决定。
