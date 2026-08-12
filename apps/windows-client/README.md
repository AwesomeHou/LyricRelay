# Windows Client

Windows Client 是 MVP 的主端，负责设备连接、歌词获取、时间轴同步、本地设置和任务栏渲染。它不播放手机音频。

## 计划模块

```text
src/
├─ Link/              # 发现、配对、连接和重连
├─ Lyrics/            # Provider Adapter、LRC 解析和缓存边界
├─ Timeline/          # 纯逻辑时间轴引擎
├─ Taskbar/           # Shell_TrayWnd、UI Automation、DPI 和绘制适配
├─ Settings/          # 本地设置与开机启动
└─ App/               # 生命周期、托盘入口和日志
tests/                # Timeline、协议、Provider 和渲染布局测试
```

## 技术约束

- 以 C# / .NET 为默认实现方向。
- 任务栏集成放在独立适配层，Windows Shell 变化不能污染歌词和链路核心逻辑。
- 时间轴核心只依赖输入状态与单调时钟，必须可以在无 Windows UI 的测试环境运行。
- 初始版本只显示单行或双行歌词，不做桌面悬浮窗口。

## 关键行为

- Windows 先根据 `title + artist + album + duration` 获取时间轴歌词，再开始显示。
- 手机状态变化立即重置或校准本地时间轴。
- 网络断开时保留短时 UI 状态；恢复后以手机最新状态重新校准。
- 找不到同步歌词时显示可理解的空状态，不阻塞设备连接。

