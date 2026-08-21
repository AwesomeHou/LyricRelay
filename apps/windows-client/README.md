# Windows Client

Windows Client 是 MVP 的主端，负责设备连接、歌词获取、时间轴同步、本地设置和任务栏渲染。它不播放手机音频。

## 工程结构

```text
src/
├─ Link/              # TLS 配对、发现、连接和重连
├─ Lyrics/            # QQ/网易云/酷狗/LRCLIB Provider、LRC 解析和请求竞态隔离
├─ Timeline/          # 纯逻辑时间轴引擎
├─ Taskbar/           # TaskbarLyrics 风格的 Shell_TrayWnd 子窗口、UI Automation 布局、DPI 和原生绘制适配
└─ Settings/          # 本地设置
tests/                # 无平台依赖的核心测试
```

## 技术约束

- 以 C# / .NET 为默认实现方向。
- 任务栏集成放在独立适配层，Windows Shell 变化不能污染歌词和链路核心逻辑。
- 时间轴核心只依赖输入状态与单调时钟，必须可以在无 Windows UI 的测试环境运行。
- 任务栏渲染采用 Shell_TrayWnd 子窗口 + UI Automation 可用间隙计算；WPF 文本控件提供轻量的 DirectWrite-backed 绘制，不使用 WebView2。
- 任务栏默认显示当前歌词原文；Provider 返回源自带翻译时显示“原文 + 翻译”两行，原文更大、翻译更小，并保留上下留白；没有翻译时只显示原文，不调用翻译服务。

## 关键行为

- Windows 先根据 `title + artist + album + duration` 获取时间轴歌词，再开始显示。
- 手机状态变化立即重置或校准本地时间轴。
- 网络断开时保留短时 UI 状态；恢复后以手机最新状态重新校准。
- 找不到同步歌词时显示可理解的空状态，不阻塞设备连接。
- 托盘图标右键菜单提供“打开设置”“重启客户端”和“退出”。重启会等待旧实例释放单实例锁后再接管，避免重复实例提示。

## 当前歌词来源

MVP 当前接入四个在线 Provider：QQ 音乐、网易云音乐、酷狗和 [LRCLIB API](https://lrclib.net/docs)。Provider 只使用歌曲 Metadata 请求时间轴歌词；QQ 音乐优先读取歌词接口的翻译字段，必要时从 QQ 歌词下载接口的 `contentts` 字段补齐，网易云音乐读取 `tlyric`，都按时间戳与原文合并；没有翻译时只显示原文，不调用翻译服务。没有时间轴歌词时不会把普通歌词伪装成同步歌词。Provider 通过 `ILyricsProvider` 隔离，暂不支持本地歌词文件。

## 本地构建和测试

需要 .NET 8 SDK 和 Windows 桌面开发组件。当前仓库验证使用 `E:\LyricRelay\.tools\dotnet-full\dotnet.exe`：

```powershell
dotnet run --project apps/windows-client/tests/LyricRelay.Core.Tests.csproj
dotnet build apps/windows-client/LyricRelay.Windows.csproj
```

Windows Client 当前已经包含真实的 TLS TCP 链路、一次性 QR 配对、局域网 UDP 发现、四个在线歌词 Adapter 和任务栏子窗口渲染代码。Core 测试和 Windows Client 编译已在本机通过；任务栏歌词仍需用实体设备播放可命中的歌曲完成最终观察。
