# SpotifyLyricsWin

Windows 上播放 Spotify 时显示桌面悬浮歌词的小插件（当前版本 v0.3）。
外观显示与逐字高亮算法按安卓版 SpotifyLyrics V2.4 复刻，规格见
[外观复刻规格.md](外观复刻规格.md)；使用方法见 [README-使用说明.md](README-使用说明.md)。

## 功能概要

- 通过 Windows SMTC 跟踪 Spotify 播放（不改 Spotify 本体、无需登录授权）
- 歌词获取：缓存 → 国内四源并行搜候选打分选优（≥50 分采用）→ 顺序回退（酷狗 KRC 真逐字 → QQ → 网易云 → 酷我 → LRCLIB → lyrics.ovh → Musixmatch），本地缓存
- 平滑逐字渐变扫光（真逐字时间戳优先，否则字符权重插值）
- 设置实时生效：字号/颜色（HSV 颜色盘）/背景/偏移/字体/单双行/锁定位置
- 广告识别（启发式 + 全量诊断日志）、手动检索并选择歌词、右击菜单、托盘常驻
- v0.3：繁简自动转简体、三窗自绘深色主题、颜色盘点外部自动关、按钮带（按钮不压歌词）、单行双居中

## 构建

环境：.NET 8 SDK。**源码为零 XAML、纯代码构建 UI**，因此可在 Linux 上交叉编译
Windows 目标（`EnableWindowsTargeting`），Windows 本机同理。

```bash
cd app
dotnet publish -c Release -r win-x64 --self-contained false -o ../publish
```

产物为框架依赖版（需 .NET 8 桌面运行时）：`SpotifyLyricsWin.exe` + dll + app.ico 等。

## 结构

- `app/Core/` — 逐字引擎（KRC 解码、LRC 解析、WordTimeline、同步引擎）与设置/日志
- `app/Lyrics/` — 七个歌词源 Provider、取词编排、手动选词候选接口
- `app/Player/` — SMTC 播放跟踪、广告识别
- `app/Ui/` — 悬浮窗、设置窗、颜色盘、手动选词窗、托盘
