# Apex F1

Apex F1 是一个 Windows 桌面观赛与赛事信息应用，使用 C#、WinUI 3 XAML 和 Windows App SDK。窗口顶部居中排列首页、新闻、积分榜、赛果、赛历和观赛六个标签。首次启动时，中文系统使用简体中文，其余系统语言使用英文。标题栏左侧可切换中英文和深浅主题，应用会记住选择。

## 构建与运行

本机生成的 x64 发布目录为 `bin\publish\win-x64-media`。打开其中的 `ApexF1.exe` 即可运行。该版本使用系统中的 .NET 10 运行时和 WebView2 Runtime。

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
- **积分榜**显示车手与车队的名次及积分，并使用 F1 官网的车手肖像和 2026 赛季领队专题照片。**赛果**和**赛历**提供官方页面的应用内入口。[车手积分榜](https://www.formula1.com/en/results/2026/drivers) · [2026 车队领队](https://www.formula1.com/en/latest/article/who-are-the-2026-team-principals.2Jrw5LEJbEoX2gM5OtFL8K)
- **观赛**提供央视赛车专区、体育节目单、CCTV-5 和 CCTV-5+ 官方直播页及央视中国大奖赛转播专题。央视转播场次以节目单和赛事预告为准。[央视赛车专区](https://sports.cctv.com/racing/) · [体育节目单](https://sports.cctv.com/epg/?channel=cctv5+)
- **创作者**可直达两个 UP 主的空间和马莎拉车的数据直播间。[F1赛事资讯](https://space.bilibili.com/65125803) · [马莎拉车](https://space.bilibili.com/1313650809) · [数据直播间](https://live.bilibili.com/23954427)

使用 `Ctrl+F` 可定位标题栏搜索框。内容在应用的网页阅读页内打开，可使用后退、前进、刷新和“在浏览器打开”。播放、登录和订阅条件由各来源平台决定。
